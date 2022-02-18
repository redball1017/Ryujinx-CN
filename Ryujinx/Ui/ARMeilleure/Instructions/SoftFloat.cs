using ARMeilleure.State;
using System;
using System.Diagnostics;

namespace ARMeilleure.Instructions
{
    static class SoftFloat
    {
        static SoftFloat()
        {
            RecipEstimateTable     = BuildRecipEstimateTable();
            RecipSqrtEstimateTable = BuildRecipSqrtEstimateTable();
        }

        internal static readonly byte[] RecipEstimateTable;
        internal static readonly byte[] RecipSqrtEstimateTable;

        private static byte[] BuildRecipEstimateTable()
        {
            byte[] tbl = new byte[256];

            for (int idx = 0; idx < 256; idx++)
            {
                uint src = (uint)idx + 256u;

                Debug.Assert(256u <= src && src < 512u);

                src = (src << 1) + 1u;

                uint aux = (1u << 19) / src;

                uint dst = (aux + 1u) >> 1;

                Debug.Assert(256u <= dst && dst < 512u);

                tbl[idx] = (byte)(dst - 256u);
            }

            return tbl;
        }

        private static byte[] BuildRecipSqrtEstimateTable()
        {
            byte[] tbl = new byte[384];

            for (int idx = 0; idx < 384; idx++)
            {
                uint src = (uint)idx + 128u;

                Debug.Assert(128u <= src && src < 512u);

                if (src < 256u)
                {
                    src = (src << 1) + 1u;
                }
                else
                {
                    src = (src >> 1) << 1;
                    src = (src + 1u) << 1;
                }

                uint aux = 512u;

                while (src * (aux + 1u) * (aux + 1u) < (1u << 28))
                {
                    aux = aux + 1u;
                }

                uint dst = (aux + 1u) >> 1;

                Debug.Assert(256u <= dst && dst < 512u);

                tbl[idx] = (byte)(dst - 256u);
            }

            return tbl;
        }
    }

    static class SoftFloat16_32
    {
        public static float FPConvert(ushort valueBits)
        {
            ExecutionContext context = NativeInterface.GetContext();

            double real = valueBits.FPUnpackCv(out FPType type, out bool sign, context);

            float result;

            if (type == FPType.SNaN || type == FPType.QNaN)
            {
                if ((context.Fpcr & FPCR.Dn) != 0)
                {
                    result = FPDefaultNaN();
                }
                else
                {
                    result = FPConvertNaN(valueBits);
                }

                if (type == FPType.SNaN)
                {
                    FPProcessException(FPException.InvalidOp, context);
                }
            }
            else if (type == FPType.Infinity)
            {
                result = FPInfinity(sign);
            }
            else if (type == FPType.Zero)
            {
                result = FPZero(sign);
            }
            else
            {
                result = FPRoundCv(real, context);
            }

            return result;
        }

        private static float FPDefaultNaN()
        {
            return BitConverter.Int32BitsToSingle(0x7fc00000);
        }

        private static float FPInfinity(bool sign)
        {
            return sign ? float.NegativeInfinity : float.PositiveInfinity;
        }

        private static float FPZero(bool sign)
        {
            return sign ? -0f : +0f;
        }

        private static float FPMaxNormal(bool sign)
        {
            return sign ? float.MinValue : float.MaxValue;
        }

        private static double FPUnpackCv(
            this ushort valueBits,
            out FPType type,
            out bool sign,
            ExecutionContext context)
        {
            sign = (~(uint)valueBits & 0x8000u) == 0u;

            uint exp16  = ((uint)valueBits & 0x7C00u) >> 10;
            uint frac16 =  (uint)valueBits & 0x03FFu;

            double real;

            if (exp16 == 0u)
            {
                if (frac16 == 0u)
                {
                    type = FPType.Zero;
                    real = 0d;
                }
                else
                {
                    type = FPType.Nonzero; // Subnormal.
                    real = Math.Pow(2d, -14) * ((double)frac16 * Math.Pow(2d, -10));
                }
            }
            else if (exp16 == 0x1Fu && (context.Fpcr & FPCR.Ahp) == 0)
            {
                if (frac16 == 0u)
                {
                    type = FPType.Infinity;
                    real = Math.Pow(2d, 1000);
                }
                else
                {
                    type = (~frac16 & 0x0200u) == 0u ? FPType.QNaN : FPType.SNaN;
                    real = 0d;
                }
            }
            else
            {
                type = FPType.Nonzero; // Normal.
                real = Math.Pow(2d, (int)exp16 - 15) * (1d + (double)frac16 * Math.Pow(2d, -10));
            }

            return sign ? -real : real;
        }

        private static float FPRoundCv(double real, ExecutionContext context)
        {
            const int minimumExp = -126;

            const int e = 8;
            const int f = 23;

            bool   sign;
            double mantissa;

            if (real < 0d)
            {
                sign     = true;
                mantissa = -real;
            }
            else
            {
                sign     = false;
                mantissa = real;
            }

            int exponent = 0;

            while (mantissa < 1d)
            {
                mantissa *= 2d;
                exponent--;
            }

            while (mantissa >= 2d)
            {
                mantissa /= 2d;
                exponent++;
            }

            if ((context.Fpcr & FPCR.Fz) != 0 && exponent < minimumExp)
            {
                context.Fpsr |= FPSR.Ufc;

                return FPZero(sign);
            }

            uint biasedExp = (uint)Math.Max(exponent - minimumExp + 1, 0);

            if (biasedExp == 0u)
            {
                mantissa /= Math.Pow(2d, minimumExp - exponent);
            }

            uint intMant = (uint)Math.Floor(mantissa * Math.Pow(2d, f));
            double error = mantissa * Math.Pow(2d, f) - (double)intMant;

            if (biasedExp == 0u && (error != 0d || (context.Fpcr & FPCR.Ufe) != 0))
            {
                FPProcessException(FPException.Underflow, context);
            }

            bool overflowToInf;
            bool roundUp;

            switch (context.Fpcr.GetRoundingMode())
            {
                default:
                case FPRoundingMode.ToNearest:
                    roundUp       = (error > 0.5d || (error == 0.5d && (intMant & 1u) == 1u));
                    overflowToInf = true;
                    break;

                case FPRoundingMode.TowardsPlusInfinity:
                    roundUp       = (error != 0d && !sign);
                    overflowToInf = !sign;
                    break;

                case FPRoundingMode.TowardsMinusInfinity:
                    roundUp       = (error != 0d && sign);
                    overflowToInf = sign;
                    break;

                case FPRoundingMode.TowardsZero:
                    roundUp       = false;
                    overflowToInf = false;
                    break;
            }

            if (roundUp)
            {
                intMant++;

                if (intMant == 1u << f)
                {
                    biasedExp = 1u;
                }

                if (intMant == 1u << (f + 1))
                {
                    biasedExp++;
                    intMant >>= 1;
                }
            }

            float result;

            if (biasedExp >= (1u << e) - 1u)
            {
                result = overflowToInf ? FPInfinity(sign) : FPMaxNormal(sign);

                FPProcessException(FPException.Overflow, context);

                error = 1d;
            }
            else
            {
                result = BitConverter.Int32BitsToSingle(
                    (int)((sign ? 1u : 0u) << 31 | (biasedExp & 0xFFu) << 23 | (intMant & 0x007FFFFFu)));
            }

            if (error != 0d)
            {
                FPProcessException(FPException.Inexact, context);
            }

            return result;
        }

        private static float FPConvertNaN(ushort valueBits)
        {
            return BitConverter.Int32BitsToSingle(
                (int)(((uint)valueBits & 0x8000u) << 16 | 0x7FC00000u | ((uint)valueBits & 0x01FFu) << 13));
        }

        private static void FPProcessException(FPException exc, ExecutionContext context)
        {
            int enable = (int)exc + 8;

            if ((context.Fpcr & (FPCR)(1 << enable)) != 0)
            {
                throw new NotImplementedException("Floating-point trap handling.");
            }
            else
            {
                context.Fpsr |= (FPSR)(1 << (int)exc);
            }
        }
    }

    static class SoftFloat32_16
    {
        public static ushort FPConvert(float value)
        {
            ExecutionContext context = NativeInterface.GetContext();

            double real = value.FPUnpackCv(out FPType type, out bool sign, out uint valueBits, context);

            bool altHp = (context.Fpcr & FPCR.Ahp) != 0;

            ushort resultBits;

            if (type == FPType.SNaN || type == FPType.QNaN)
            {
                if (altHp)
                {
                    resultBits = FPZero(sign);
                }
                else if ((context.Fpcr & FPCR.Dn) != 0)
                {
                    resultBits = FPDefaultNaN();
                }
                else
                {
                    resultBits = FPConvertNaN(valueBits);
                }

                if (type == FPType.SNaN || altHp)
                {
                    FPProcessException(FPException.InvalidOp, context);
                }
            }
            else if (type == FPType.Infinity)
            {
                if (altHp)
                {
                    resultBits = (ushort)((sign ? 1u : 0u) << 15 | 0x7FFFu);

                    FPProcessException(FPException.InvalidOp, context);
                }
                else
                {
                    resultBits = FPInfinity(sign);
                }
            }
            else if (type == FPType.Zero)
            {
                resultBits = FPZero(sign);
            }
            else
            {
                resultBits = FPRoundCv(real, context);
            }

            return resultBits;
        }

        private static ushort FPDefaultNaN()
        {
            return (ushort)0x7E00u;
        }

        private static ushort FPInfinity(bool sign)
        {
            return sign ? (ushort)0xFC00u : (ushort)0x7C00u;
        }

        private static ushort FPZero(bool sign)
        {
            return sign ? (ushort)0x8000u : (ushort)0x0000u;
        }

        private static ushort FPMaxNormal(bool sign)
        {
            return sign ? (ushort)0xFBFFu : (ushort)0x7BFFu;
        }

        private static double FPUnpackCv(
            this float value,
            out FPType type,
            out bool sign,
            out uint valueBits,
            ExecutionContext context)
        {
            valueBits = (uint)BitConverter.SingleToInt32Bits(value);

            sign = (~valueBits & 0x80000000u) == 0u;

            uint exp32  = (valueBits & 0x7F800000u) >> 23;
            uint frac32 =  valueBits & 0x007FFFFFu;

            double real;

            if (exp32 == 0u)
            {
                if (frac32 == 0u || (context.Fpcr & FPCR.Fz) != 0)
                {
                    type = FPType.Zero;
                    real = 0d;

                    if (frac32 != 0u)
                    {
                        FPProcessException(FPException.InputDenorm, context);
                    }
                }
                else
                {
                    type = FPType.Nonzero; // Subnormal.
                    real = Math.Pow(2d, -126) * ((double)frac32 * Math.Pow(2d, -23));
                }
            }
            else if (exp32 == 0xFFu)
            {
                if (frac32 == 0u)
                {
                    type = FPType.Infinity;
                    real = Math.Pow(2d, 1000);
                }
                else
                {
                    type = (~frac32 & 0x00400000u) == 0u ? FPType.QNaN : FPType.SNaN;
                    real = 0d;
                }
            }
            else
            {
                type = FPType.Nonzero; // Normal.
                real = Math.Pow(2d, (int)exp32 - 127) * (1d + (double)frac32 * Math.Pow(2d, -23));
            }

            return sign ? -real : real;
        }

        private static ushort FPRoundCv(double real, ExecutionContext context)
        {
            const int minimumExp = -14;

            const int e = 5;
            const int f = 10;

            bool   sign;
            double mantissa;

            if (real < 0d)
            {
                sign     = true;
                mantissa = -real;
            }
            else
            {
                sign     = false;
                mantissa = real;
            }

            int exponent = 0;

            while (mantissa < 1d)
            {
                mantissa *= 2d;
                exponent--;
            }

            while (mantissa >= 2d)
            {
                mantissa /= 2d;
                exponent++;
            }

            uint biasedExp = (uint)Math.Max(exponent - minimumExp + 1, 0);

            if (biasedExp == 0u)
            {
                mantissa /= Math.Pow(2d, minimumExp - exponent);
            }

            uint intMant = (uint)Math.Floor(mantissa * Math.Pow(2d, f));
            double error = mantissa * Math.Pow(2d, f) - (double)intMant;

            if (biasedExp == 0u && (error != 0d || (context.Fpcr & FPCR.Ufe) != 0))
            {
                FPProcessException(FPException.Underflow, context);
            }

            bool overflowToInf;
            bool roundUp;

            switch (context.Fpcr.GetRoundingMode())
            {
                default:
                case FPRoundingMode.ToNearest:
                    roundUp       = (error > 0.5d || (error == 0.5d && (intMant & 1u) == 1u));
                    overflowToInf = true;
                    break;

                case FPRoundingMode.TowardsPlusInfinity:
                    roundUp       = (error != 0d && !sign);
                    overflowToInf = !sign;
                    break;

                case FPRoundingMode.TowardsMinusInfinity:
                    roundUp       = (error != 0d && sign);
                    overflowToInf = sign;
                    break;

                case FPRoundingMode.TowardsZero:
                    roundUp       = false;
                    overflowToInf = false;
                    break;
            }

            if (roundUp)
            {
                intMant++;

                if (intMant == 1u << f)
                {
                    biasedExp = 1u;
                }

                if (intMant == 1u << (f + 1))
                {
                    biasedExp++;
                    intMant >>= 1;
                }
            }

            ushort resultBits;

            if ((context.Fpcr & FPCR.Ahp) == 0)
            {
                if (biasedExp >= (1u << e) - 1u)
                {
                    resultBits = overflowToInf ? FPInfinity(sign) : FPMaxNormal(sign);

                    FPProcessException(FPException.Overflow, context);

                    error = 1d;
                }
                else
                {
                    resultBits = (ushort)((sign ? 1u : 0u) << 15 | (biasedExp & 0x1Fu) << 10 | (intMant & 0x03FFu));
                }
            }
            else
            {
                if (biasedExp >= 1u << e)
                {
                    resultBits = (ushort)((sign ? 1u : 0u) << 15 | 0x7FFFu);

                    FPProcessException(FPException.InvalidOp, context);

                    error = 0d;
                }
                else
                {
                    resultBits = (ushort)((sign ? 1u : 0u) << 15 | (biasedExp & 0x1Fu) << 10 | (intMant & 0x03FFu));
                }
            }

            if (error != 0d)
            {
                FPProcessException(FPException.Inexact, context);
            }

            return resultBits;
        }

        private static ushort FPConvertNaN(uint valueBits)
        {
            return (ushort)((valueBits & 0x80000000u) >> 16 | 0x7E00u | (valueBits & 0x003FE000u) >> 13);
        }

        private static void FPProcessException(FPException exc, ExecutionContext context)
        {
            int enable = (int)exc + 8;

            if ((context.Fpcr & (FPCR)(1 << enable)) != 0)
            {
                throw new NotImplementedException("Floating-point trap handling.");
            }
            else
            {
                context.Fpsr |= (FPSR)(1 << (int)exc);
            }
        }
    }

    static class SoftFloat32
    {
        public static float FPAdd(float value1, float value2)
        {
            return FPAddFpscr(value1, value2, false);
        }

        public static float FPAddFpscr(float value1, float value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out uint op2, context, fpcr);

            float result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity; bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity; bool zero2 = type2 == FPType.Zero;

                if (inf1 && inf2 && sign1 == !sign2)
                {
                    result = FPDefaultNaN();

                    FPProcessException(FPException.InvalidOp, context, fpcr);
                }
                else if ((inf1 && !sign1) || (inf2 && !sign2))
                {
                    result = FPInfinity(false);
                }
                else if ((inf1 && sign1) || (inf2 && sign2))
                {
                    result = FPInfinity(true);
                }
                else if (zero1 && zero2 && sign1 == sign2)
                {
                    result = FPZero(sign1);
                }
                else
                {
                    result = value1 + value2;

                    if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0f);
                    }
                }
            }

            return result;
        }

        public static int FPCompare(float value1, float value2, bool signalNaNs)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out _, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out _, context, fpcr);

            int result;

            if (type1 == FPType.SNaN || type1 == FPType.QNaN || type2 == FPType.SNaN || type2 == FPType.QNaN)
            {
                result = 0b0011;

                if (type1 == FPType.SNaN || type2 == FPType.SNaN || signalNaNs)
                {
                    FPProcessException(FPException.InvalidOp, context, fpcr);
                }
            }
            else
            {
                if (value1 == value2)
                {
                    result = 0b0110;
                }
                else if (value1 < value2)
                {
                    result = 0b1000;
                }
                else
                {
                    result = 0b0010;
                }
            }

            return result;
        }

        public static float FPCompareEQ(float value1, float value2)
        {
            return FPCompareEQFpscr(value1, value2, false);
        }

        public static float FPCompareEQFpscr(float value1, float value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out _, out _, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out _, out _, context, fpcr);

            float result;

            if (type1 == FPType.SNaN || type1 == FPType.QNaN || type2 == FPType.SNaN || type2 == FPType.QNaN)
            {
                result = ZerosOrOnes(false);

                if (type1 == FPType.SNaN || type2 == FPType.SNaN)
                {
                    FPProcessException(FPException.InvalidOp, context, fpcr);
                }
            }
            else
            {
                result = ZerosOrOnes(value1 == value2);
            }

            return result;
        }

        public static float FPCompareGE(float value1, float value2)
        {
            return FPCompareGEFpscr(value1, value2, false);
        }

        public static float FPCompareGEFpscr(float value1, float value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out _, out _, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out _, out _, context, fpcr);

            float result;

            if (type1 == FPType.SNaN || type1 == FPType.QNaN || type2 == FPType.SNaN || type2 == FPType.QNaN)
            {
                result = ZerosOrOnes(false);

                FPProcessException(FPException.InvalidOp, context, fpcr);
            }
            else
            {
                result = ZerosOrOnes(value1 >= value2);
            }

            return result;
        }

        public static float FPCompareGT(float value1, float value2)
        {
            return FPCompareGTFpscr(value1, value2, false);
        }

        public static float FPCompareGTFpscr(float value1, float value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out _, out _, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out _, out _, context, fpcr);

            float result;

            if (type1 == FPType.SNaN || type1 == FPType.QNaN || type2 == FPType.SNaN || type2 == FPType.QNaN)
            {
                result = ZerosOrOnes(false);

                FPProcessException(FPException.InvalidOp, context, fpcr);
            }
            else
            {
                result = ZerosOrOnes(value1 > value2);
            }

            return result;
        }

        public static float FPCompareLE(float value1, float value2)
        {
            return FPCompareGE(value2, value1);
        }

        public static float FPCompareLT(float value1, float value2)
        {
            return FPCompareGT(value2, value1);
        }

        public static float FPCompareLEFpscr(float value1, float value2, bool standardFpscr)
        {
            return FPCompareGEFpscr(value2, value1, standardFpscr);
        }

        public static float FPCompareLTFpscr(float value1, float value2, bool standardFpscr)
        {
            return FPCompareGTFpscr(value2, value1, standardFpscr);
        }

        public static float FPDiv(float value1, float value2)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out uint op2, context, fpcr);

            float result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity; bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity; bool zero2 = type2 == FPType.Zero;

                if ((inf1 && inf2) || (zero1 && zero2))
                {
                    result = FPDefaultNaN();

                    FPProcessException(FPException.InvalidOp, context, fpcr);
                }
                else if (inf1 || zero2)
                {
                    result = FPInfinity(sign1 ^ sign2);

                    if (!inf1)
                    {
                        FPProcessException(FPException.DivideByZero, context, fpcr);
                    }
                }
                else if (zero1 || inf2)
                {
                    result = FPZero(sign1 ^ sign2);
                }
                else
                {
                    result = value1 / value2;

                    if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0f);
                    }
                }
            }

            return result;
        }

        public static float FPMax(float value1, float value2)
        {
            return FPMaxFpscr(value1, value2, false);
        }

        public static float FPMaxFpscr(float value1, float value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out uint op2, context, fpcr);

            float result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                if (value1 > value2)
                {
                    if (type1 == FPType.Infinity)
                    {
                        result = FPInfinity(sign1);
                    }
                    else if (type1 == FPType.Zero)
                    {
                        result = FPZero(sign1 && sign2);
                    }
                    else
                    {
                        result = value1;

                        if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                        {
                            context.Fpsr |= FPSR.Ufc;

                            result = FPZero(result < 0f);
                        }
                    }
                }
                else
                {
                    if (type2 == FPType.Infinity)
                    {
                        result = FPInfinity(sign2);
                    }
                    else if (type2 == FPType.Zero)
                    {
                        result = FPZero(sign1 && sign2);
                    }
                    else
                    {
                        result = value2;

                        if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                        {
                            context.Fpsr |= FPSR.Ufc;

                            result = FPZero(result < 0f);
                        }
                    }
                }
            }

            return result;
        }

        public static float FPMaxNum(float value1, float value2)
        {
            return FPMaxNumFpscr(value1, value2, false);
        }

        public static float FPMaxNumFpscr(float value1, float value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1.FPUnpack(out FPType type1, out _, out _, context, fpcr);
            value2.FPUnpack(out FPType type2, out _, out _, context, fpcr);

            if (type1 == FPType.QNaN && type2 != FPType.QNaN)
            {
                value1 = FPInfinity(true);
            }
            else if (type1 != FPType.QNaN && type2 == FPType.QNaN)
            {
                value2 = FPInfinity(true);
            }

            return FPMaxFpscr(value1, value2, standardFpscr);
        }

        public static float FPMin(float value1, float value2)
        {
            return FPMinFpscr(value1, value2, false);
        }

        public static float FPMinFpscr(float value1, float value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out uint op2, context, fpcr);

            float result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                if (value1 < value2)
                {
                    if (type1 == FPType.Infinity)
                    {
                        result = FPInfinity(sign1);
                    }
                    else if (type1 == FPType.Zero)
                    {
                        result = FPZero(sign1 || sign2);
                    }
                    else
                    {
                        result = value1;

                        if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                        {
                            context.Fpsr |= FPSR.Ufc;

                            result = FPZero(result < 0f);
                        }
                    }
                }
                else
                {
                    if (type2 == FPType.Infinity)
                    {
                        result = FPInfinity(sign2);
                    }
                    else if (type2 == FPType.Zero)
                    {
                        result = FPZero(sign1 || sign2);
                    }
                    else
                    {
                        result = value2;

                        if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                        {
                            context.Fpsr |= FPSR.Ufc;

                            result = FPZero(result < 0f);
                        }
                    }
                }
            }

            return result;
        }

        public static float FPMinNum(float value1, float value2)
        {
            return FPMinNumFpscr(value1, value2, false);
        }

        public static float FPMinNumFpscr(float value1, float value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1.FPUnpack(out FPType type1, out _, out _, context, fpcr);
            value2.FPUnpack(out FPType type2, out _, out _, context, fpcr);

            if (type1 == FPType.QNaN && type2 != FPType.QNaN)
            {
                value1 = FPInfinity(false);
            }
            else if (type1 != FPType.QNaN && type2 == FPType.QNaN)
            {
                value2 = FPInfinity(false);
            }

            return FPMinFpscr(value1, value2, standardFpscr);
        }

        public static float FPMul(float value1, float value2)
        {
            return FPMulFpscr(value1, value2, false);
        }

        public static float FPMulFpscr(float value1, float value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out uint op2, context, fpcr);

            float result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity; bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity; bool zero2 = type2 == FPType.Zero;

                if ((inf1 && zero2) || (zero1 && inf2))
                {
                    result = FPDefaultNaN();

                    FPProcessException(FPException.InvalidOp, context, fpcr);
                }
                else if (inf1 || inf2)
                {
                    result = FPInfinity(sign1 ^ sign2);
                }
                else if (zero1 || zero2)
                {
                    result = FPZero(sign1 ^ sign2);
                }
                else
                {
                    result = value1 * value2;

                    if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0f);
                    }
                }
            }

            return result;
        }

        public static float FPMulAdd(float valueA, float value1, float value2)
        {
            return FPMulAddFpscr(valueA, value1, value2, false);
        }

        public static float FPMulAddFpscr(float valueA, float value1, float value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            valueA = valueA.FPUnpack(out FPType typeA, out bool signA, out uint addend, context, fpcr);
            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out uint op1,    context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out uint op2,    context, fpcr);

            bool inf1 = type1 == FPType.Infinity; bool zero1 = type1 == FPType.Zero;
            bool inf2 = type2 == FPType.Infinity; bool zero2 = type2 == FPType.Zero;

            float result = FPProcessNaNs3(typeA, type1, type2, addend, op1, op2, out bool done, context, fpcr);

            if (typeA == FPType.QNaN && ((inf1 && zero2) || (zero1 && inf2)))
            {
                result = FPDefaultNaN();

                FPProcessException(FPException.InvalidOp, context, fpcr);
            }

            if (!done)
            {
                bool infA = typeA == FPType.Infinity; bool zeroA = typeA == FPType.Zero;

                bool signP = sign1 ^  sign2;
                bool infP  = inf1  || inf2;
                bool zeroP = zero1 || zero2;

                if ((inf1 && zero2) || (zero1 && inf2) || (infA && infP && signA != signP))
                {
                    result = FPDefaultNaN();

                    FPProcessException(FPException.InvalidOp, context, fpcr);
                }
                else if ((infA && !signA) || (infP && !signP))
                {
                    result = FPInfinity(false);
                }
                else if ((infA && signA) || (infP && signP))
                {
                    result = FPInfinity(true);
                }
                else if (zeroA && zeroP && signA == signP)
                {
                    result = FPZero(signA);
                }
                else
                {
                    result = MathF.FusedMultiplyAdd(value1, value2, valueA);

                    if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0f);
                    }
                }
            }

            return result;
        }

        public static float FPMulSub(float valueA, float value1, float value2)
        {
            value1 = value1.FPNeg();

            return FPMulAdd(valueA, value1, value2);
        }

        public static float FPMulSubFpscr(float valueA, float value1, float value2, bool standardFpscr)
        {
            value1 = value1.FPNeg();

            return FPMulAddFpscr(valueA, value1, value2, standardFpscr);
        }

        public static float FPMulX(float value1, float value2)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out uint op2, context, fpcr);

            float result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity; bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity; bool zero2 = type2 == FPType.Zero;

                if ((inf1 && zero2) || (zero1 && inf2))
                {
                    result = FPTwo(sign1 ^ sign2);
                }
                else if (inf1 || inf2)
                {
                    result = FPInfinity(sign1 ^ sign2);
                }
                else if (zero1 || zero2)
                {
                    result = FPZero(sign1 ^ sign2);
                }
                else
                {
                    result = value1 * value2;

                    if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0f);
                    }
                }
            }

            return result;
        }

        public static float FPNegMulAdd(float valueA, float value1, float value2)
        {
            valueA = valueA.FPNeg();
            value1 = value1.FPNeg();

            return FPMulAdd(valueA, value1, value2);
        }

        public static float FPNegMulSub(float valueA, float value1, float value2)
        {
            valueA = valueA.FPNeg();

            return FPMulAdd(valueA, value1, value2);
        }

        public static float FPRecipEstimate(float value)
        {
            return FPRecipEstimateFpscr(value, false);
        }

        public static float FPRecipEstimateFpscr(float value, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value.FPUnpack(out FPType type, out bool sign, out uint op, context, fpcr);

            float result;

            if (type == FPType.SNaN || type == FPType.QNaN)
            {
                result = FPProcessNaN(type, op, context, fpcr);
            }
            else if (type == FPType.Infinity)
            {
                result = FPZero(sign);
            }
            else if (type == FPType.Zero)
            {
                result = FPInfinity(sign);

                FPProcessException(FPException.DivideByZero, context, fpcr);
            }
            else if (MathF.Abs(value) < MathF.Pow(2f, -128))
            {
                bool overflowToInf;

                switch (fpcr.GetRoundingMode())
                {
                    default:
                    case FPRoundingMode.ToNearest:            overflowToInf = true;  break;
                    case FPRoundingMode.TowardsPlusInfinity:  overflowToInf = !sign; break;
                    case FPRoundingMode.TowardsMinusInfinity: overflowToInf = sign;  break;
                    case FPRoundingMode.TowardsZero:          overflowToInf = false; break;
                }

                result = overflowToInf ? FPInfinity(sign) : FPMaxNormal(sign);

                FPProcessException(FPException.Overflow, context, fpcr);
                FPProcessException(FPException.Inexact,  context, fpcr);
            }
            else if ((fpcr & FPCR.Fz) != 0 && (MathF.Abs(value) >= MathF.Pow(2f, 126)))
            {
                result = FPZero(sign);

                context.Fpsr |= FPSR.Ufc;
            }
            else
            {
                ulong fraction = (ulong)(op & 0x007FFFFFu) << 29;
                uint exp = (op & 0x7F800000u) >> 23;

                if (exp == 0u)
                {
                    if ((fraction & 0x0008000000000000ul) == 0ul)
                    {
                        fraction = (fraction & 0x0003FFFFFFFFFFFFul) << 2;
                        exp -= 1u;
                    }
                    else
                    {
                        fraction = (fraction & 0x0007FFFFFFFFFFFFul) << 1;
                    }
                }

                uint scaled = (uint)(((fraction & 0x000FF00000000000ul) | 0x0010000000000000ul) >> 44);

                uint resultExp = 253u - exp;

                uint estimate = (uint)SoftFloat.RecipEstimateTable[scaled - 256u] + 256u;

                fraction = (ulong)(estimate & 0xFFu) << 44;

                if (resultExp == 0u)
                {
                    fraction = ((fraction & 0x000FFFFFFFFFFFFEul) | 0x0010000000000000ul) >> 1;
                }
                else if (resultExp + 1u == 0u)
                {
                    fraction = ((fraction & 0x000FFFFFFFFFFFFCul) | 0x0010000000000000ul) >> 2;
                    resultExp = 0u;
                }

                result = BitConverter.Int32BitsToSingle(
                    (int)((sign ? 1u : 0u) << 31 | (resultExp & 0xFFu) << 23 | (uint)(fraction >> 29) & 0x007FFFFFu));
            }

            return result;
        }

        public static float FPRecipStep(float value1, float value2)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.StandardFpcrValue;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out uint op2, context, fpcr);

            float result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity; bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity; bool zero2 = type2 == FPType.Zero;

                float product;

                if ((inf1 && zero2) || (zero1 && inf2))
                {
                    product = FPZero(false);
                }
                else
                {
                    product = FPMulFpscr(value1, value2, true);
                }

                result = FPSubFpscr(FPTwo(false), product, true);
            }

            return result;
        }

        public static float FPRecipStepFused(float value1, float value2)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value1 = value1.FPNeg();

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out uint op2, context, fpcr);

            float result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity; bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity; bool zero2 = type2 == FPType.Zero;

                if ((inf1 && zero2) || (zero1 && inf2))
                {
                    result = FPTwo(false);
                }
                else if (inf1 || inf2)
                {
                    result = FPInfinity(sign1 ^ sign2);
                }
                else
                {
                    result = MathF.FusedMultiplyAdd(value1, value2, 2f);

                    if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0f);
                    }
                }
            }

            return result;
        }

        public static float FPRecpX(float value)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value.FPUnpack(out FPType type, out bool sign, out uint op, context, fpcr);

            float result;

            if (type == FPType.SNaN || type == FPType.QNaN)
            {
                result = FPProcessNaN(type, op, context, fpcr);
            }
            else
            {
                uint notExp = (~op >> 23) & 0xFFu;
                uint maxExp = 0xFEu;

                result = BitConverter.Int32BitsToSingle(
                    (int)((sign ? 1u : 0u) << 31 | (notExp == 0xFFu ? maxExp : notExp) << 23));
            }

            return result;
        }

        public static float FPRSqrtEstimate(float value)
        {
            return FPRSqrtEstimateFpscr(value, false);
        }

        public static float FPRSqrtEstimateFpscr(float value, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value.FPUnpack(out FPType type, out bool sign, out uint op, context, fpcr);

            float result;

            if (type == FPType.SNaN || type == FPType.QNaN)
            {
                result = FPProcessNaN(type, op, context, fpcr);
            }
            else if (type == FPType.Zero)
            {
                result = FPInfinity(sign);

                FPProcessException(FPException.DivideByZero, context, fpcr);
            }
            else if (sign)
            {
                result = FPDefaultNaN();

                FPProcessException(FPException.InvalidOp, context, fpcr);
            }
            else if (type == FPType.Infinity)
            {
                result = FPZero(false);
            }
            else
            {
                ulong fraction = (ulong)(op & 0x007FFFFFu) << 29;
                uint exp = (op & 0x7F800000u) >> 23;

                if (exp == 0u)
                {
                    while ((fraction & 0x0008000000000000ul) == 0ul)
                    {
                        fraction = (fraction & 0x0007FFFFFFFFFFFFul) << 1;
                        exp -= 1u;
                    }

                    fraction = (fraction & 0x0007FFFFFFFFFFFFul) << 1;
                }

                uint scaled;

                if ((exp & 1u) == 0u)
                {
                    scaled = (uint)(((fraction & 0x000FF00000000000ul) | 0x0010000000000000ul) >> 44);
                }
                else
                {
                    scaled = (uint)(((fraction & 0x000FE00000000000ul) | 0x0010000000000000ul) >> 45);
                }

                uint resultExp = (380u - exp) >> 1;

                uint estimate = (uint)SoftFloat.RecipSqrtEstimateTable[scaled - 128u] + 256u;

                result = BitConverter.Int32BitsToSingle((int)((resultExp & 0xFFu) << 23 | (estimate & 0xFFu) << 15));
            }

            return result;
        }

        public static float FPHalvedSub(float value1, float value2, ExecutionContext context, FPCR fpcr)
        {
            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out uint op2, context, fpcr);

            float result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity; bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity; bool zero2 = type2 == FPType.Zero;

                if (inf1 && inf2 && sign1 == sign2)
                {
                    result = FPDefaultNaN();

                    FPProcessException(FPException.InvalidOp, context, fpcr);
                }
                else if ((inf1 && !sign1) || (inf2 && sign2))
                {
                    result = FPInfinity(false);
                }
                else if ((inf1 && sign1) || (inf2 && !sign2))
                {
                    result = FPInfinity(true);
                }
                else if (zero1 && zero2 && sign1 == !sign2)
                {
                    result = FPZero(sign1);
                }
                else
                {
                    result = (value1 - value2) / 2.0f;

                    if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0f);
                    }
                }
            }

            return result;
        }

        public static float FPRSqrtStep(float value1, float value2)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.StandardFpcrValue;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out uint op2, context, fpcr);

            float result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity; bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity; bool zero2 = type2 == FPType.Zero;

                float product;

                if ((inf1 && zero2) || (zero1 && inf2))
                {
                    product = FPZero(false);
                }
                else
                {
                    product = FPMulFpscr(value1, value2, true);
                }
                
                result = FPHalvedSub(FPThree(false), product, context, fpcr);
            }

            return result;
        }

        public static float FPRSqrtStepFused(float value1, float value2)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value1 = value1.FPNeg();

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out uint op2, context, fpcr);

            float result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity; bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity; bool zero2 = type2 == FPType.Zero;

                if ((inf1 && zero2) || (zero1 && inf2))
                {
                    result = FPOnePointFive(false);
                }
                else if (inf1 || inf2)
                {
                    result = FPInfinity(sign1 ^ sign2);
                }
                else
                {
                    result = MathF.FusedMultiplyAdd(value1, value2, 3f) / 2f;

                    if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0f);
                    }
                }
            }

            return result;
        }

        public static float FPSqrt(float value)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value = value.FPUnpack(out FPType type, out bool sign, out uint op, context, fpcr);

            float result;

            if (type == FPType.SNaN || type == FPType.QNaN)
            {
                result = FPProcessNaN(type, op, context, fpcr);
            }
            else if (type == FPType.Zero)
            {
                result = FPZero(sign);
            }
            else if (type == FPType.Infinity && !sign)
            {
                result = FPInfinity(sign);
            }
            else if (sign)
            {
                result = FPDefaultNaN();

                FPProcessException(FPException.InvalidOp, context, fpcr);
            }
            else
            {
                result = MathF.Sqrt(value);

                if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                {
                    context.Fpsr |= FPSR.Ufc;

                    result = FPZero(result < 0f);
                }
            }

            return result;
        }

        public static float FPSub(float value1, float value2)
        {
            return FPSubFpscr(value1, value2, false);
        }

        public static float FPSubFpscr(float value1, float value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out uint op2, context, fpcr);

            float result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity; bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity; bool zero2 = type2 == FPType.Zero;

                if (inf1 && inf2 && sign1 == sign2)
                {
                    result = FPDefaultNaN();

                    FPProcessException(FPException.InvalidOp, context, fpcr);
                }
                else if ((inf1 && !sign1) || (inf2 && sign2))
                {
                    result = FPInfinity(false);
                }
                else if ((inf1 && sign1) || (inf2 && !sign2))
                {
                    result = FPInfinity(true);
                }
                else if (zero1 && zero2 && sign1 == !sign2)
                {
                    result = FPZero(sign1);
                }
                else
                {
                    result = value1 - value2;

                    if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0f);
                    }
                }
            }

            return result;
        }

        private static float FPDefaultNaN()
        {
            return BitConverter.Int32BitsToSingle(0x7fc00000);
        }

        private static float FPInfinity(bool sign)
        {
            return sign ? float.NegativeInfinity : float.PositiveInfinity;
        }

        private static float FPZero(bool sign)
        {
            return sign ? -0f : +0f;
        }

        private static float FPMaxNormal(bool sign)
        {
            return sign ? float.MinValue : float.MaxValue;
        }

        private static float FPTwo(bool sign)
        {
            return sign ? -2f : +2f;
        }

        private static float FPThree(bool sign)
        {
            return sign ? -3f : +3f;
        }

        private static float FPOnePointFive(bool sign)
        {
            return sign ? -1.5f : +1.5f;
        }

        private static float FPNeg(this float value)
        {
            return -value;
        }

        private static float ZerosOrOnes(bool ones)
        {
            return BitConverter.Int32BitsToSingle(ones ? -1 : 0);
        }

        private static float FPUnpack(
            this float value,
            out FPType type,
            out bool sign,
            out uint valueBits,
            ExecutionContext context,
            FPCR fpcr)
        {
            valueBits = (uint)BitConverter.SingleToInt32Bits(value);

            sign = (~valueBits & 0x80000000u) == 0u;

            if ((valueBits & 0x7F800000u) == 0u)
            {
                if ((valueBits & 0x007FFFFFu) == 0u || (fpcr & FPCR.Fz) != 0)
                {
                    type  = FPType.Zero;
                    value = FPZero(sign);

                    if ((valueBits & 0x007FFFFFu) != 0u)
                    {
                        FPProcessException(FPException.InputDenorm, context, fpcr);
                    }
                }
                else
                {
                    type = FPType.Nonzero;
                }
            }
            else if ((~valueBits & 0x7F800000u) == 0u)
            {
                if ((valueBits & 0x007FFFFFu) == 0u)
                {
                    type = FPType.Infinity;
                }
                else
                {
                    type  = (~valueBits & 0x00400000u) == 0u ? FPType.QNaN : FPType.SNaN;
                    value = FPZero(sign);
                }
            }
            else
            {
                type = FPType.Nonzero;
            }

            return value;
        }

        private static float FPProcessNaNs(
            FPType type1,
            FPType type2,
            uint op1,
            uint op2,
            out bool done,
            ExecutionContext context,
            FPCR fpcr)
        {
            done = true;

            if (type1 == FPType.SNaN)
            {
                return FPProcessNaN(type1, op1, context, fpcr);
            }
            else if (type2 == FPType.SNaN)
            {
                return FPProcessNaN(type2, op2, context, fpcr);
            }
            else if (type1 == FPType.QNaN)
            {
                return FPProcessNaN(type1, op1, context, fpcr);
            }
            else if (type2 == FPType.QNaN)
            {
                return FPProcessNaN(type2, op2, context, fpcr);
            }

            done = false;

            return FPZero(false);
        }

        private static float FPProcessNaNs3(
            FPType type1,
            FPType type2,
            FPType type3,
            uint op1,
            uint op2,
            uint op3,
            out bool done,
            ExecutionContext context,
            FPCR fpcr)
        {
            done = true;

            if (type1 == FPType.SNaN)
            {
                return FPProcessNaN(type1, op1, context, fpcr);
            }
            else if (type2 == FPType.SNaN)
            {
                return FPProcessNaN(type2, op2, context, fpcr);
            }
            else if (type3 == FPType.SNaN)
            {
                return FPProcessNaN(type3, op3, context, fpcr);
            }
            else if (type1 == FPType.QNaN)
            {
                return FPProcessNaN(type1, op1, context, fpcr);
            }
            else if (type2 == FPType.QNaN)
            {
                return FPProcessNaN(type2, op2, context, fpcr);
            }
            else if (type3 == FPType.QNaN)
            {
                return FPProcessNaN(type3, op3, context, fpcr);
            }

            done = false;

            return FPZero(false);
        }

        private static float FPProcessNaN(FPType type, uint op, ExecutionContext context, FPCR fpcr)
        {
            if (type == FPType.SNaN)
            {
                op |= 1u << 22;

                FPProcessException(FPException.InvalidOp, context, fpcr);
            }

            if ((fpcr & FPCR.Dn) != 0)
            {
                return FPDefaultNaN();
            }

            return BitConverter.Int32BitsToSingle((int)op);
        }

        private static void FPProcessException(FPException exc, ExecutionContext context, FPCR fpcr)
        {
            int enable = (int)exc + 8;

            if ((fpcr & (FPCR)(1 << enable)) != 0)
            {
                throw new NotImplementedException("Floating-point trap handling.");
            }
            else
            {
                context.Fpsr |= (FPSR)(1 << (int)exc);
            }
        }
    }

    static class SoftFloat64
    {
        public static double FPAdd(double value1, double value2)
        {
            return FPAddFpscr(value1, value2, false);
        }

        public static double FPAddFpscr(double value1, double value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out ulong op2, context, fpcr);

            double result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity; bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity; bool zero2 = type2 == FPType.Zero;

                if (inf1 && inf2 && sign1 == !sign2)
                {
                    result = FPDefaultNaN();

                    FPProcessException(FPException.InvalidOp, context, fpcr);
                }
                else if ((inf1 && !sign1) || (inf2 && !sign2))
                {
                    result = FPInfinity(false);
                }
                else if ((inf1 && sign1) || (inf2 && sign2))
                {
                    result = FPInfinity(true);
                }
                else if (zero1 && zero2 && sign1 == sign2)
                {
                    result = FPZero(sign1);
                }
                else
                {
                    result = value1 + value2;

                    if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0d);
                    }
                }
            }

            return result;
        }

        public static int FPCompare(double value1, double value2, bool signalNaNs)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out _, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out _, context, fpcr);

            int result;

            if (type1 == FPType.SNaN || type1 == FPType.QNaN || type2 == FPType.SNaN || type2 == FPType.QNaN)
            {
                result = 0b0011;

                if (type1 == FPType.SNaN || type2 == FPType.SNaN || signalNaNs)
                {
                    FPProcessException(FPException.InvalidOp, context, fpcr);
                }
            }
            else
            {
                if (value1 == value2)
                {
                    result = 0b0110;
                }
                else if (value1 < value2)
                {
                    result = 0b1000;
                }
                else
                {
                    result = 0b0010;
                }
            }

            return result;
        }

        public static double FPCompareEQ(double value1, double value2)
        {
            return FPCompareEQFpscr(value1, value2, false);
        }

        public static double FPCompareEQFpscr(double value1, double value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out _, out _, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out _, out _, context, fpcr);

            double result;

            if (type1 == FPType.SNaN || type1 == FPType.QNaN || type2 == FPType.SNaN || type2 == FPType.QNaN)
            {
                result = ZerosOrOnes(false);

                if (type1 == FPType.SNaN || type2 == FPType.SNaN)
                {
                    FPProcessException(FPException.InvalidOp, context, fpcr);
                }
            }
            else
            {
                result = ZerosOrOnes(value1 == value2);
            }

            return result;
        }

        public static double FPCompareGE(double value1, double value2)
        {
            return FPCompareGEFpscr(value1, value2, false);
        }

        public static double FPCompareGEFpscr(double value1, double value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out _, out _, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out _, out _, context, fpcr);

            double result;

            if (type1 == FPType.SNaN || type1 == FPType.QNaN || type2 == FPType.SNaN || type2 == FPType.QNaN)
            {
                result = ZerosOrOnes(false);

                FPProcessException(FPException.InvalidOp, context, fpcr);
            }
            else
            {
                result = ZerosOrOnes(value1 >= value2);
            }

            return result;
        }

        public static double FPCompareGT(double value1, double value2)
        {
            return FPCompareGTFpscr(value1, value2, false);
        }

        public static double FPCompareGTFpscr(double value1, double value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out _, out _, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out _, out _, context, fpcr);

            double result;

            if (type1 == FPType.SNaN || type1 == FPType.QNaN || type2 == FPType.SNaN || type2 == FPType.QNaN)
            {
                result = ZerosOrOnes(false);

                FPProcessException(FPException.InvalidOp, context, fpcr);
            }
            else
            {
                result = ZerosOrOnes(value1 > value2);
            }

            return result;
        }

        public static double FPCompareLE(double value1, double value2)
        {
            return FPCompareGE(value2, value1);
        }

        public static double FPCompareLT(double value1, double value2)
        {
            return FPCompareGT(value2, value1);
        }

        public static double FPCompareLEFpscr(double value1, double value2, bool standardFpscr)
        {
            return FPCompareGEFpscr(value2, value1, standardFpscr);
        }

        public static double FPCompareLTFpscr(double value1, double value2, bool standardFpscr)
        {
            return FPCompareGTFpscr(value2, value1, standardFpscr);
        }

        public static double FPDiv(double value1, double value2)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out ulong op2, context, fpcr);

            double result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity; bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity; bool zero2 = type2 == FPType.Zero;

                if ((inf1 && inf2) || (zero1 && zero2))
                {
                    result = FPDefaultNaN();

                    FPProcessException(FPException.InvalidOp, context, fpcr);
                }
                else if (inf1 || zero2)
                {
                    result = FPInfinity(sign1 ^ sign2);

                    if (!inf1)
                    {
                        FPProcessException(FPException.DivideByZero, context, fpcr);
                    }
                }
                else if (zero1 || inf2)
                {
                    result = FPZero(sign1 ^ sign2);
                }
                else
                {
                    result = value1 / value2;

                    if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0d);
                    }
                }
            }

            return result;
        }

        public static double FPMax(double value1, double value2)
        {
            return FPMaxFpscr(value1, value2, false);
        }

        public static double FPMaxFpscr(double value1, double value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out ulong op2, context, fpcr);

            double result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                if (value1 > value2)
                {
                    if (type1 == FPType.Infinity)
                    {
                        result = FPInfinity(sign1);
                    }
                    else if (type1 == FPType.Zero)
                    {
                        result = FPZero(sign1 && sign2);
                    }
                    else
                    {
                        result = value1;

                        if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                        {
                            context.Fpsr |= FPSR.Ufc;

                            result = FPZero(result < 0d);
                        }
                    }
                }
                else
                {
                    if (type2 == FPType.Infinity)
                    {
                        result = FPInfinity(sign2);
                    }
                    else if (type2 == FPType.Zero)
                    {
                        result = FPZero(sign1 && sign2);
                    }
                    else
                    {
                        result = value2;

                        if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                        {
                            context.Fpsr |= FPSR.Ufc;

                            result = FPZero(result < 0d);
                        }
                    }
                }
            }

            return result;
        }

        public static double FPMaxNum(double value1, double value2)
        {
            return FPMaxNumFpscr(value1, value2, false);
        }

        public static double FPMaxNumFpscr(double value1, double value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1.FPUnpack(out FPType type1, out _, out _, context, fpcr);
            value2.FPUnpack(out FPType type2, out _, out _, context, fpcr);

            if (type1 == FPType.QNaN && type2 != FPType.QNaN)
            {
                value1 = FPInfinity(true);
            }
            else if (type1 != FPType.QNaN && type2 == FPType.QNaN)
            {
                value2 = FPInfinity(true);
            }

            return FPMaxFpscr(value1, value2, standardFpscr);
        }

        public static double FPMin(double value1, double value2)
        {
            return FPMinFpscr(value1, value2, false);
        }

        public static double FPMinFpscr(double value1, double value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out ulong op2, context, fpcr);

            double result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                if (value1 < value2)
                {
                    if (type1 == FPType.Infinity)
                    {
                        result = FPInfinity(sign1);
                    }
                    else if (type1 == FPType.Zero)
                    {
                        result = FPZero(sign1 || sign2);
                    }
                    else
                    {
                        result = value1;

                        if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                        {
                            context.Fpsr |= FPSR.Ufc;

                            result = FPZero(result < 0d);
                        }
                    }
                }
                else
                {
                    if (type2 == FPType.Infinity)
                    {
                        result = FPInfinity(sign2);
                    }
                    else if (type2 == FPType.Zero)
                    {
                        result = FPZero(sign1 || sign2);
                    }
                    else
                    {
                        result = value2;

                        if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                        {
                            context.Fpsr |= FPSR.Ufc;

                            result = FPZero(result < 0d);
                        }
                    }
                }
            }

            return result;
        }

        public static double FPMinNum(double value1, double value2)
        {
            return FPMinNumFpscr(value1, value2, false);
        }

        public static double FPMinNumFpscr(double value1, double value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1.FPUnpack(out FPType type1, out _, out _, context, fpcr);
            value2.FPUnpack(out FPType type2, out _, out _, context, fpcr);

            if (type1 == FPType.QNaN && type2 != FPType.QNaN)
            {
                value1 = FPInfinity(false);
            }
            else if (type1 != FPType.QNaN && type2 == FPType.QNaN)
            {
                value2 = FPInfinity(false);
            }

            return FPMinFpscr(value1, value2, standardFpscr);
        }

        public static double FPMul(double value1, double value2)
        {
            return FPMulFpscr(value1, value2, false);
        }

        public static double FPMulFpscr(double value1, double value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out ulong op2, context, fpcr);

            double result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity; bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity; bool zero2 = type2 == FPType.Zero;

                if ((inf1 && zero2) || (zero1 && inf2))
                {
                    result = FPDefaultNaN();

                    FPProcessException(FPException.InvalidOp, context, fpcr);
                }
                else if (inf1 || inf2)
                {
                    result = FPInfinity(sign1 ^ sign2);
                }
                else if (zero1 || zero2)
                {
                    result = FPZero(sign1 ^ sign2);
                }
                else
                {
                    result = value1 * value2;

                    if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0d);
                    }
                }
            }

            return result;
        }

        public static double FPMulAdd(double valueA, double value1, double value2)
        {
            return FPMulAddFpscr(valueA, value1, value2, false);
        }

        public static double FPMulAddFpscr(double valueA, double value1, double value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            valueA = valueA.FPUnpack(out FPType typeA, out bool signA, out ulong addend, context, fpcr);
            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out ulong op1,    context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out ulong op2,    context, fpcr);

            bool inf1 = type1 == FPType.Infinity; bool zero1 = type1 == FPType.Zero;
            bool inf2 = type2 == FPType.Infinity; bool zero2 = type2 == FPType.Zero;

            double result = FPProcessNaNs3(typeA, type1, type2, addend, op1, op2, out bool done, context, fpcr);

            if (typeA == FPType.QNaN && ((inf1 && zero2) || (zero1 && inf2)))
            {
                result = FPDefaultNaN();

                FPProcessException(FPException.InvalidOp, context, fpcr);
            }

            if (!done)
            {
                bool infA = typeA == FPType.Infinity; bool zeroA = typeA == FPType.Zero;

                bool signP = sign1 ^  sign2;
                bool infP  = inf1  || inf2;
                bool zeroP = zero1 || zero2;

                if ((inf1 && zero2) || (zero1 && inf2) || (infA && infP && signA != signP))
                {
                    result = FPDefaultNaN();

                    FPProcessException(FPException.InvalidOp, context, fpcr);
                }
                else if ((infA && !signA) || (infP && !signP))
                {
                    result = FPInfinity(false);
                }
                else if ((infA && signA) || (infP && signP))
                {
                    result = FPInfinity(true);
                }
                else if (zeroA && zeroP && signA == signP)
                {
                    result = FPZero(signA);
                }
                else
                {
                    result = Math.FusedMultiplyAdd(value1, value2, valueA);

                    if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0d);
                    }
                }
            }

            return result;
        }

        public static double FPMulSub(double valueA, double value1, double value2)
        {
            value1 = value1.FPNeg();

            return FPMulAdd(valueA, value1, value2);
        }

        public static double FPMulSubFpscr(double valueA, double value1, double value2, bool standardFpscr)
        {
            value1 = value1.FPNeg();

            return FPMulAddFpscr(valueA, value1, value2, standardFpscr);
        }

        public static double FPMulX(double value1, double value2)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out ulong op2, context, fpcr);

            double result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity; bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity; bool zero2 = type2 == FPType.Zero;

                if ((inf1 && zero2) || (zero1 && inf2))
                {
                    result = FPTwo(sign1 ^ sign2);
                }
                else if (inf1 || inf2)
                {
                    result = FPInfinity(sign1 ^ sign2);
                }
                else if (zero1 || zero2)
                {
                    result = FPZero(sign1 ^ sign2);
                }
                else
                {
                    result = value1 * value2;

                    if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0d);
                    }
                }
            }

            return result;
        }

        public static double FPNegMulAdd(double valueA, double value1, double value2)
        {
            valueA = valueA.FPNeg();
            value1 = value1.FPNeg();

            return FPMulAdd(valueA, value1, value2);
        }

        public static double FPNegMulSub(double valueA, double value1, double value2)
        {
            valueA = valueA.FPNeg();

            return FPMulAdd(valueA, value1, value2);
        }

        public static double FPRecipEstimate(double value)
        {
            return FPRecipEstimateFpscr(value, false);
        }

        public static double FPRecipEstimateFpscr(double value, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value.FPUnpack(out FPType type, out bool sign, out ulong op, context, fpcr);

            double result;

            if (type == FPType.SNaN || type == FPType.QNaN)
            {
                result = FPProcessNaN(type, op, context, fpcr);
            }
            else if (type == FPType.Infinity)
            {
                result = FPZero(sign);
            }
            else if (type == FPType.Zero)
            {
                result = FPInfinity(sign);

                FPProcessException(FPException.DivideByZero, context, fpcr);
            }
            else if (Math.Abs(value) < Math.Pow(2d, -1024))
            {
                bool overflowToInf;

                switch (fpcr.GetRoundingMode())
                {
                    default:
                    case FPRoundingMode.ToNearest:            overflowToInf = true;  break;
                    case FPRoundingMode.TowardsPlusInfinity:  overflowToInf = !sign; break;
                    case FPRoundingMode.TowardsMinusInfinity: overflowToInf = sign;  break;
                    case FPRoundingMode.TowardsZero:          overflowToInf = false; break;
                }

                result = overflowToInf ? FPInfinity(sign) : FPMaxNormal(sign);

                FPProcessException(FPException.Overflow, context, fpcr);
                FPProcessException(FPException.Inexact,  context, fpcr);
            }
            else if ((fpcr & FPCR.Fz) != 0 && (Math.Abs(value) >= Math.Pow(2d, 1022)))
            {
                result = FPZero(sign);

                context.Fpsr |= FPSR.Ufc;
            }
            else
            {
                ulong fraction = op & 0x000FFFFFFFFFFFFFul;
                uint exp = (uint)((op & 0x7FF0000000000000ul) >> 52);

                if (exp == 0u)
                {
                    if ((fraction & 0x0008000000000000ul) == 0ul)
                    {
                        fraction = (fraction & 0x0003FFFFFFFFFFFFul) << 2;
                        exp -= 1u;
                    }
                    else
                    {
                        fraction = (fraction & 0x0007FFFFFFFFFFFFul) << 1;
                    }
                }

                uint scaled = (uint)(((fraction & 0x000FF00000000000ul) | 0x0010000000000000ul) >> 44);

                uint resultExp = 2045u - exp;

                uint estimate = (uint)SoftFloat.RecipEstimateTable[scaled - 256u] + 256u;

                fraction = (ulong)(estimate & 0xFFu) << 44;

                if (resultExp == 0u)
                {
                    fraction = ((fraction & 0x000FFFFFFFFFFFFEul) | 0x0010000000000000ul) >> 1;
                }
                else if (resultExp + 1u == 0u)
                {
                    fraction = ((fraction & 0x000FFFFFFFFFFFFCul) | 0x0010000000000000ul) >> 2;
                    resultExp = 0u;
                }

                result = BitConverter.Int64BitsToDouble(
                    (long)((sign ? 1ul : 0ul) << 63 | (resultExp & 0x7FFul) << 52 | (fraction & 0x000FFFFFFFFFFFFFul)));
            }

            return result;
        }

        public static double FPRecipStep(double value1, double value2)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.StandardFpcrValue;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out ulong op2, context, fpcr);

            double result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity; bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity; bool zero2 = type2 == FPType.Zero;

                double product;

                if ((inf1 && zero2) || (zero1 && inf2))
                {
                    product = FPZero(false);
                }
                else
                {
                    product = FPMulFpscr(value1, value2, true);
                }

                result = FPSubFpscr(FPTwo(false), product, true);
            }

            return result;
        }

        public static double FPRecipStepFused(double value1, double value2)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value1 = value1.FPNeg();

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out ulong op2, context, fpcr);

            double result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity; bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity; bool zero2 = type2 == FPType.Zero;

                if ((inf1 && zero2) || (zero1 && inf2))
                {
                    result = FPTwo(false);
                }
                else if (inf1 || inf2)
                {
                    result = FPInfinity(sign1 ^ sign2);
                }
                else
                {
                    result = Math.FusedMultiplyAdd(value1, value2, 2d);

                    if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0d);
                    }
                }
            }

            return result;
        }

        public static double FPRecpX(double value)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value.FPUnpack(out FPType type, out bool sign, out ulong op, context, fpcr);

            double result;

            if (type == FPType.SNaN || type == FPType.QNaN)
            {
                result = FPProcessNaN(type, op, context, fpcr);
            }
            else
            {
                ulong notExp = (~op >> 52) & 0x7FFul;
                ulong maxExp = 0x7FEul;

                result = BitConverter.Int64BitsToDouble(
                    (long)((sign ? 1ul : 0ul) << 63 | (notExp == 0x7FFul ? maxExp : notExp) << 52));
            }

            return result;
        }

        public static double FPRSqrtEstimate(double value)
        {
            return FPRSqrtEstimateFpscr(value, false);
        }

        public static double FPRSqrtEstimateFpscr(double value, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value.FPUnpack(out FPType type, out bool sign, out ulong op, context, fpcr);

            double result;

            if (type == FPType.SNaN || type == FPType.QNaN)
            {
                result = FPProcessNaN(type, op, context, fpcr);
            }
            else if (type == FPType.Zero)
            {
                result = FPInfinity(sign);

                FPProcessException(FPException.DivideByZero, context, fpcr);
            }
            else if (sign)
            {
                result = FPDefaultNaN();

                FPProcessException(FPException.InvalidOp, context, fpcr);
            }
            else if (type == FPType.Infinity)
            {
                result = FPZero(false);
            }
            else
            {
                ulong fraction = op & 0x000FFFFFFFFFFFFFul;
                uint exp = (uint)((op & 0x7FF0000000000000ul) >> 52);

                if (exp == 0u)
                {
                    while ((fraction & 0x0008000000000000ul) == 0ul)
                    {
                        fraction = (fraction & 0x0007FFFFFFFFFFFFul) << 1;
                        exp -= 1u;
                    }

                    fraction = (fraction & 0x0007FFFFFFFFFFFFul) << 1;
                }

                uint scaled;

                if ((exp & 1u) == 0u)
                {
                    scaled = (uint)(((fraction & 0x000FF00000000000ul) | 0x0010000000000000ul) >> 44);
                }
                else
                {
                    scaled = (uint)(((fraction & 0x000FE00000000000ul) | 0x0010000000000000ul) >> 45);
                }

                uint resultExp = (3068u - exp) >> 1;

                uint estimate = (uint)SoftFloat.RecipSqrtEstimateTable[scaled - 128u] + 256u;

                result = BitConverter.Int64BitsToDouble((long)((resultExp & 0x7FFul) << 52 | (estimate & 0xFFul) << 44));
            }

            return result;
        }

        public static double FPHalvedSub(double value1, double value2, ExecutionContext context, FPCR fpcr)
        {
            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out ulong op2, context, fpcr);

            double result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity; bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity; bool zero2 = type2 == FPType.Zero;

                if (inf1 && inf2 && sign1 == sign2)
                {
                    result = FPDefaultNaN();

                    FPProcessException(FPException.InvalidOp, context, fpcr);
                }
                else if ((inf1 && !sign1) || (inf2 && sign2))
                {
                    result = FPInfinity(false);
                }
                else if ((inf1 && sign1) || (inf2 && !sign2))
                {
                    result = FPInfinity(true);
                }
                else if (zero1 && zero2 && sign1 == !sign2)
                {
                    result = FPZero(sign1);
                }
                else
                {
                    result = (value1 - value2) / 2.0;

                    if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0d);
                    }
                }
            }

            return result;
        }

        public static double FPRSqrtStep(double value1, double value2)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.StandardFpcrValue;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out ulong op2, context, fpcr);

            double result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity; bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity; bool zero2 = type2 == FPType.Zero;

                double product;

                if ((inf1 && zero2) || (zero1 && inf2))
                {
                    product = FPZero(false);
                }
                else
                {
                    product = FPMulFpscr(value1, value2, true);
                }

                result = FPHalvedSub(FPThree(false), product, context, fpcr);
            }

            return result;
        }

        public static double FPRSqrtStepFused(double value1, double value2)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value1 = value1.FPNeg();

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out ulong op2, context, fpcr);

            double result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity; bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity; bool zero2 = type2 == FPType.Zero;

                if ((inf1 && zero2) || (zero1 && inf2))
                {
                    result = FPOnePointFive(false);
                }
                else if (inf1 || inf2)
                {
                    result = FPInfinity(sign1 ^ sign2);
                }
                else
                {
                    result = Math.FusedMultiplyAdd(value1, value2, 3d) / 2d;

                    if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0d);
                    }
                }
            }

            return result;
        }

        public static double FPSqrt(double value)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value = value.FPUnpack(out FPType type, out bool sign, out ulong op, context, fpcr);

            double result;

            if (type == FPType.SNaN || type == FPType.QNaN)
            {
                result = FPProcessNaN(type, op, context, fpcr);
            }
            else if (type == FPType.Zero)
            {
                result = FPZero(sign);
            }
            else if (type == FPType.Infinity && !sign)
            {
                result = FPInfinity(sign);
            }
            else if (sign)
            {
                result = FPDefaultNaN();

                FPProcessException(FPException.InvalidOp, context, fpcr);
            }
            else
            {
                result = Math.Sqrt(value);

                if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                {
                    context.Fpsr |= FPSR.Ufc;

                    result = FPZero(result < 0d);
                }
            }

            return result;
        }

        public static double FPSub(double value1, double value2)
        {
            return FPSubFpscr(value1, value2, false);
        }

        public static double FPSubFpscr(double value1, double value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out ulong op2, context, fpcr);

            double result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity; bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity; bool zero2 = type2 == FPType.Zero;

                if (inf1 && inf2 && sign1 == sign2)
                {
                    result = FPDefaultNaN();

                    FPProcessException(FPException.InvalidOp, context, fpcr);
                }
                else if ((inf1 && !sign1) || (inf2 && sign2))
                {
                    result = FPInfinity(false);
                }
                else if ((inf1 && sign1) || (inf2 && !sign2))
                {
                    result = FPInfinity(true);
                }
                else if (zero1 && zero2 && sign1 == !sign2)
                {
                    result = FPZero(sign1);
                }
                else
                {
                    result = value1 - value2;

                    if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0d);
                    }
                }
            }

            return result;
        }

        private static double FPDefaultNaN()
        {
            return BitConverter.Int64BitsToDouble(0x7ff8000000000000);
        }

        private static double FPInfinity(bool sign)
        {
            return sign ? double.NegativeInfinity : double.PositiveInfinity;
        }

        private static double FPZero(bool sign)
        {
            return sign ? -0d : +0d;
        }

        private static double FPMaxNormal(bool sign)
        {
            return sign ? double.MinValue : double.MaxValue;
        }

        private static double FPTwo(bool sign)
        {
            return sign ? -2d : +2d;
        }

        private static double FPThree(bool sign)
        {
            return sign ? -3d : +3d;
        }

        private static double FPOnePointFive(bool sign)
        {
            return sign ? -1.5d : +1.5d;
        }

        private static double FPNeg(this double value)
        {
            return -value;
        }

        private static double ZerosOrOnes(bool ones)
        {
            return BitConverter.Int64BitsToDouble(ones ? -1L : 0L);
        }

        private static double FPUnpack(
            this double value,
            out FPType type,
            out bool sign,
            out ulong valueBits,
            ExecutionContext context,
            FPCR fpcr)
        {
            valueBits = (ulong)BitConverter.DoubleToInt64Bits(value);

            sign = (~valueBits & 0x8000000000000000ul) == 0ul;

            if ((valueBits & 0x7FF0000000000000ul) == 0ul)
            {
                if ((valueBits & 0x000FFFFFFFFFFFFFul) == 0ul || (fpcr & FPCR.Fz) != 0)
                {
                    type  = FPType.Zero;
                    value = FPZero(sign);

                    if ((valueBits & 0x000FFFFFFFFFFFFFul) != 0ul)
                    {
                        FPProcessException(FPException.InputDenorm, context, fpcr);
                    }
                }
                else
                {
                    type = FPType.Nonzero;
                }
            }
            else if ((~valueBits & 0x7FF0000000000000ul) == 0ul)
            {
                if ((valueBits & 0x000FFFFFFFFFFFFFul) == 0ul)
                {
                    type = FPType.Infinity;
                }
                else
                {
                    type  = (~valueBits & 0x0008000000000000ul) == 0ul ? FPType.QNaN : FPType.SNaN;
                    value = FPZero(sign);
                }
            }
            else
            {
                type = FPType.Nonzero;
            }

            return value;
        }

        private static double FPProcessNaNs(
            FPType type1,
            FPType type2,
            ulong op1,
            ulong op2,
            out bool done,
            ExecutionContext context,
            FPCR fpcr)
        {
            done = true;

            if (type1 == FPType.SNaN)
            {
                return FPProcessNaN(type1, op1, context, fpcr);
            }
            else if (type2 == FPType.SNaN)
            {
                return FPProcessNaN(type2, op2, context, fpcr);
            }
            else if (type1 == FPType.QNaN)
            {
                return FPProcessNaN(type1, op1, context, fpcr);
            }
            else if (type2 == FPType.QNaN)
            {
                return FPProcessNaN(type2, op2, context, fpcr);
            }

            done = false;

            return FPZero(false);
        }

        private static double FPProcessNaNs3(
            FPType type1,
            FPType type2,
            FPType type3,
            ulong op1,
            ulong op2,
            ulong op3,
            out bool done,
            ExecutionContext context,
            FPCR fpcr)
        {
            done = true;

            if (type1 == FPType.SNaN)
            {
                return FPProcessNaN(type1, op1, context, fpcr);
            }
            else if (type2 == FPType.SNaN)
            {
                return FPProcessNaN(type2, op2, context, fpcr);
            }
            else if (type3 == FPType.SNaN)
            {
                return FPProcessNaN(type3, op3, context, fpcr);
            }
            else if (type1 == FPType.QNaN)
            {
                return FPProcessNaN(type1, op1, context, fpcr);
            }
            else if (type2 == FPType.QNaN)
            {
                return FPProcessNaN(type2, op2, context, fpcr);
            }
            else if (type3 == FPType.QNaN)
            {
                return FPProcessNaN(type3, op3, context, fpcr);
            }

            done = false;

            return FPZero(false);
        }

        private static double FPProcessNaN(FPType type, ulong op, ExecutionContext context, FPCR fpcr)
        {
            if (type == FPType.SNaN)
            {
                op |= 1ul << 51;

                FPProcessException(FPException.InvalidOp, context, fpcr);
            }

            if ((fpcr & FPCR.Dn) != 0)
            {
                return FPDefaultNaN();
            }

            return BitConverter.Int64BitsToDouble((long)op);
        }

        private static void FPProcessException(FPException exc, ExecutionContext context, FPCR fpcr)
        {
            int enable = (int)exc + 8;

            if ((fpcr & (FPCR)(1 << enable)) != 0)
            {
                throw new NotImplementedException("Floating-point trap handling.");
            }
            else
            {
                context.Fpsr |= (FPSR)(1 << (int)exc);
            }
        }
    }
}
