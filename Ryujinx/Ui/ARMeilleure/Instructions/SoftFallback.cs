using ARMeilleure.State;
using System;

namespace ARMeilleure.Instructions
{
    static class SoftFallback
    {
#region "ShlReg"
        public static long SignedShlReg(long value, long shift, bool round, int size)
        {
            int eSize = 8 << size;

            int shiftLsB = (sbyte)shift;

            if (shiftLsB < 0)
            {
                return SignedShrReg(value, -shiftLsB, round, eSize);
            }
            else if (shiftLsB > 0)
            {
                if (shiftLsB >= eSize)
                {
                    return 0L;
                }

                return value << shiftLsB;
            }
            else /* if (shiftLsB == 0) */
            {
                return value;
            }
        }

        public static ulong UnsignedShlReg(ulong value, ulong shift, bool round, int size)
        {
            int eSize = 8 << size;

            int shiftLsB = (sbyte)shift;

            if (shiftLsB < 0)
            {
                return UnsignedShrReg(value, -shiftLsB, round, eSize);
            }
            else if (shiftLsB > 0)
            {
                if (shiftLsB >= eSize)
                {
                    return 0UL;
                }

                return value << shiftLsB;
            }
            else /* if (shiftLsB == 0) */
            {
                return value;
            }
        }

        public static long SignedShlRegSatQ(long value, long shift, bool round, int size)
        {
            ExecutionContext context = NativeInterface.GetContext();

            int eSize = 8 << size;

            int shiftLsB = (sbyte)shift;

            if (shiftLsB < 0)
            {
                return SignedShrReg(value, -shiftLsB, round, eSize);
            }
            else if (shiftLsB > 0)
            {
                if (shiftLsB >= eSize)
                {
                    return SignedSignSatQ(value, eSize, context);
                }

                if (eSize == 64)
                {
                    long shl = value << shiftLsB;
                    long shr = shl   >> shiftLsB;

                    if (shr != value)
                    {
                        return SignedSignSatQ(value, eSize, context);
                    }
                    else /* if (shr == value) */
                    {
                        return shl;
                    }
                }
                else /* if (eSize != 64) */
                {
                    return SignedSrcSignedDstSatQ(value << shiftLsB, size);
                }
            }
            else /* if (shiftLsB == 0) */
            {
                return value;
            }
        }

        public static ulong UnsignedShlRegSatQ(ulong value, ulong shift, bool round, int size)
        {
            ExecutionContext context = NativeInterface.GetContext();

            int eSize = 8 << size;

            int shiftLsB = (sbyte)shift;

            if (shiftLsB < 0)
            {
                return UnsignedShrReg(value, -shiftLsB, round, eSize);
            }
            else if (shiftLsB > 0)
            {
                if (shiftLsB >= eSize)
                {
                    return UnsignedSignSatQ(value, eSize, context);
                }

                if (eSize == 64)
                {
                    ulong shl = value << shiftLsB;
                    ulong shr = shl   >> shiftLsB;

                    if (shr != value)
                    {
                        return UnsignedSignSatQ(value, eSize, context);
                    }
                    else /* if (shr == value) */
                    {
                        return shl;
                    }
                }
                else /* if (eSize != 64) */
                {
                    return UnsignedSrcUnsignedDstSatQ(value << shiftLsB, size);
                }
            }
            else /* if (shiftLsB == 0) */
            {
                return value;
            }
        }

        private static long SignedShrReg(long value, int shift, bool round, int eSize) // shift := [1, 128]; eSize := {8, 16, 32, 64}.
        {
            if (round)
            {
                if (shift >= eSize)
                {
                    return 0L;
                }

                long roundConst = 1L << (shift - 1);

                long add = value + roundConst;

                if (eSize == 64)
                {
                    if ((~value & (value ^ add)) < 0L)
                    {
                        return (long)((ulong)add >> shift);
                    }
                    else
                    {
                        return add >> shift;
                    }
                }
                else /* if (eSize != 64) */
                {
                    return add >> shift;
                }
            }
            else /* if (!round) */
            {
                if (shift >= eSize)
                {
                    if (value < 0L)
                    {
                        return -1L;
                    }
                    else /* if (value >= 0L) */
                    {
                        return 0L;
                    }
                }

                return value >> shift;
            }
        }

        private static ulong UnsignedShrReg(ulong value, int shift, bool round, int eSize) // shift := [1, 128]; eSize := {8, 16, 32, 64}.
        {
            if (round)
            {
                if (shift > 64)
                {
                    return 0UL;
                }

                ulong roundConst = 1UL << (shift - 1);

                ulong add = value + roundConst;

                if (eSize == 64)
                {
                    if ((add < value) && (add < roundConst))
                    {
                        if (shift == 64)
                        {
                            return 1UL;
                        }

                        return (add >> shift) | (0x8000000000000000UL >> (shift - 1));
                    }
                    else
                    {
                        if (shift == 64)
                        {
                            return 0UL;
                        }

                        return add >> shift;
                    }
                }
                else /* if (eSize != 64) */
                {
                    if (shift == 64)
                    {
                        return 0UL;
                    }

                    return add >> shift;
                }
            }
            else /* if (!round) */
            {
                if (shift >= eSize)
                {
                    return 0UL;
                }

                return value >> shift;
            }
        }

        private static long SignedSignSatQ(long op, int eSize, ExecutionContext context) // eSize := {8, 16, 32, 64}.
        {
            long tMaxValue =  (1L << (eSize - 1)) - 1L;
            long tMinValue = -(1L << (eSize - 1));

            if (op > 0L)
            {
                context.Fpsr |= FPSR.Qc;

                return tMaxValue;
            }
            else if (op < 0L)
            {
                context.Fpsr |= FPSR.Qc;

                return tMinValue;
            }
            else
            {
                return 0L;
            }
        }

        private static ulong UnsignedSignSatQ(ulong op, int eSize, ExecutionContext context) // eSize := {8, 16, 32, 64}.
        {
            ulong tMaxValue = ulong.MaxValue >> (64 - eSize);

            if (op > 0UL)
            {
                context.Fpsr |= FPSR.Qc;

                return tMaxValue;
            }
            else
            {
                return 0UL;
            }
        }
#endregion

#region "ShrImm64"
        public static long SignedShrImm64(long value, long roundConst, int shift)
        {
            if (roundConst == 0L)
            {
                if (shift <= 63)
                {
                    return value >> shift;
                }
                else /* if (shift == 64) */
                {
                    if (value < 0L)
                    {
                        return -1L;
                    }
                    else /* if (value >= 0L) */
                    {
                        return 0L;
                    }
                }
            }
            else /* if (roundConst == 1L << (shift - 1)) */
            {
                if (shift <= 63)
                {
                    long add = value + roundConst;

                    if ((~value & (value ^ add)) < 0L)
                    {
                        return (long)((ulong)add >> shift);
                    }
                    else
                    {
                        return add >> shift;
                    }
                }
                else /* if (shift == 64) */
                {
                    return 0L;
                }
            }
        }

        public static ulong UnsignedShrImm64(ulong value, long roundConst, int shift)
        {
            if (roundConst == 0L)
            {
                if (shift <= 63)
                {
                    return value >> shift;
                }
                else /* if (shift == 64) */
                {
                    return 0UL;
                }
            }
            else /* if (roundConst == 1L << (shift - 1)) */
            {
                ulong add = value + (ulong)roundConst;

                if ((add < value) && (add < (ulong)roundConst))
                {
                    if (shift <= 63)
                    {
                        return (add >> shift) | (0x8000000000000000UL >> (shift - 1));
                    }
                    else /* if (shift == 64) */
                    {
                        return 1UL;
                    }
                }
                else
                {
                    if (shift <= 63)
                    {
                        return add >> shift;
                    }
                    else /* if (shift == 64) */
                    {
                        return 0UL;
                    }
                }
            }
        }
#endregion

#region "Rounding"
        public static double Round(double value)
        {
            ExecutionContext context = NativeInterface.GetContext();

            FPRoundingMode roundMode = context.Fpcr.GetRoundingMode();

            if (roundMode == FPRoundingMode.ToNearest)
            {
                return Math.Round(value); // even
            }
            else if (roundMode == FPRoundingMode.TowardsPlusInfinity)
            {
                return Math.Ceiling(value);
            }
            else if (roundMode == FPRoundingMode.TowardsMinusInfinity)
            {
                return Math.Floor(value);
            }
            else /* if (roundMode == FPRoundingMode.TowardsZero) */
            {
                return Math.Truncate(value);
            }
        }

        public static float RoundF(float value)
        {
            ExecutionContext context = NativeInterface.GetContext();

            FPRoundingMode roundMode = context.Fpcr.GetRoundingMode();

            if (roundMode == FPRoundingMode.ToNearest)
            {
                return MathF.Round(value); // even
            }
            else if (roundMode == FPRoundingMode.TowardsPlusInfinity)
            {
                return MathF.Ceiling(value);
            }
            else if (roundMode == FPRoundingMode.TowardsMinusInfinity)
            {
                return MathF.Floor(value);
            }
            else /* if (roundMode == FPRoundingMode.TowardsZero) */
            {
                return MathF.Truncate(value);
            }
        }

        public static int FloatToInt32(float value)
        {
            return SatF32ToS32(RoundF(value));
        }

        public static int DoubleToInt32(double value)
        {
            return SatF64ToS32(Round(value));
        }

        public static uint FloatToUInt32(float value)
        {
            return SatF32ToU32(RoundF(value));
        }

        public static uint DoubleToUInt32(double value)
        {
            return SatF64ToU32(Round(value));
        }
#endregion

#region "Saturation"
        public static int SatF32ToS32(float value)
        {
            if (float.IsNaN(value)) return 0;

            return value >= int.MaxValue ? int.MaxValue :
                   value <= int.MinValue ? int.MinValue : (int)value;
        }

        public static long SatF32ToS64(float value)
        {
            if (float.IsNaN(value)) return 0;

            return value >= long.MaxValue ? long.MaxValue :
                   value <= long.MinValue ? long.MinValue : (long)value;
        }

        public static uint SatF32ToU32(float value)
        {
            if (float.IsNaN(value)) return 0;

            return value >= uint.MaxValue ? uint.MaxValue :
                   value <= uint.MinValue ? uint.MinValue : (uint)value;
        }

        public static ulong SatF32ToU64(float value)
        {
            if (float.IsNaN(value)) return 0;

            return value >= ulong.MaxValue ? ulong.MaxValue :
                   value <= ulong.MinValue ? ulong.MinValue : (ulong)value;
        }

        public static int SatF64ToS32(double value)
        {
            if (double.IsNaN(value)) return 0;

            return value >= int.MaxValue ? int.MaxValue :
                   value <= int.MinValue ? int.MinValue : (int)value;
        }

        public static long SatF64ToS64(double value)
        {
            if (double.IsNaN(value)) return 0;

            return value >= long.MaxValue ? long.MaxValue :
                   value <= long.MinValue ? long.MinValue : (long)value;
        }

        public static uint SatF64ToU32(double value)
        {
            if (double.IsNaN(value)) return 0;

            return value >= uint.MaxValue ? uint.MaxValue :
                   value <= uint.MinValue ? uint.MinValue : (uint)value;
        }

        public static ulong SatF64ToU64(double value)
        {
            if (double.IsNaN(value)) return 0;

            return value >= ulong.MaxValue ? ulong.MaxValue :
                   value <= ulong.MinValue ? ulong.MinValue : (ulong)value;
        }
#endregion

#region "Saturating"
        public static long SignedSrcSignedDstSatQ(long op, int size)
        {
            ExecutionContext context = NativeInterface.GetContext();

            int eSize = 8 << size;

            long tMaxValue =  (1L << (eSize - 1)) - 1L;
            long tMinValue = -(1L << (eSize - 1));

            if (op > tMaxValue)
            {
                context.Fpsr |= FPSR.Qc;

                return tMaxValue;
            }
            else if (op < tMinValue)
            {
                context.Fpsr |= FPSR.Qc;

                return tMinValue;
            }
            else
            {
                return op;
            }
        }

        public static ulong SignedSrcUnsignedDstSatQ(long op, int size)
        {
            ExecutionContext context = NativeInterface.GetContext();

            int eSize = 8 << size;

            ulong tMaxValue = (1UL << eSize) - 1UL;
            ulong tMinValue =  0UL;

            if (op > (long)tMaxValue)
            {
                context.Fpsr |= FPSR.Qc;

                return tMaxValue;
            }
            else if (op < (long)tMinValue)
            {
                context.Fpsr |= FPSR.Qc;

                return tMinValue;
            }
            else
            {
                return (ulong)op;
            }
        }

        public static long UnsignedSrcSignedDstSatQ(ulong op, int size)
        {
            ExecutionContext context = NativeInterface.GetContext();

            int eSize = 8 << size;

            long tMaxValue = (1L << (eSize - 1)) - 1L;

            if (op > (ulong)tMaxValue)
            {
                context.Fpsr |= FPSR.Qc;

                return tMaxValue;
            }
            else
            {
                return (long)op;
            }
        }

        public static ulong UnsignedSrcUnsignedDstSatQ(ulong op, int size)
        {
            ExecutionContext context = NativeInterface.GetContext();

            int eSize = 8 << size;

            ulong tMaxValue = (1UL << eSize) - 1UL;

            if (op > tMaxValue)
            {
                context.Fpsr |= FPSR.Qc;

                return tMaxValue;
            }
            else
            {
                return op;
            }
        }

        public static long UnarySignedSatQAbsOrNeg(long op)
        {
            ExecutionContext context = NativeInterface.GetContext();

            if (op == long.MinValue)
            {
                context.Fpsr |= FPSR.Qc;

                return long.MaxValue;
            }
            else
            {
                return op;
            }
        }

        public static long BinarySignedSatQAdd(long op1, long op2)
        {
            ExecutionContext context = NativeInterface.GetContext();

            long add = op1 + op2;

            if ((~(op1 ^ op2) & (op1 ^ add)) < 0L)
            {
                context.Fpsr |= FPSR.Qc;

                if (op1 < 0L)
                {
                    return long.MinValue;
                }
                else
                {
                    return long.MaxValue;
                }
            }
            else
            {
                return add;
            }
        }

        public static ulong BinaryUnsignedSatQAdd(ulong op1, ulong op2)
        {
            ExecutionContext context = NativeInterface.GetContext();

            ulong add = op1 + op2;

            if ((add < op1) && (add < op2))
            {
                context.Fpsr |= FPSR.Qc;

                return ulong.MaxValue;
            }
            else
            {
                return add;
            }
        }

        public static long BinarySignedSatQSub(long op1, long op2)
        {
            ExecutionContext context = NativeInterface.GetContext();

            long sub = op1 - op2;

            if (((op1 ^ op2) & (op1 ^ sub)) < 0L)
            {
                context.Fpsr |= FPSR.Qc;

                if (op1 < 0L)
                {
                    return long.MinValue;
                }
                else
                {
                    return long.MaxValue;
                }
            }
            else
            {
                return sub;
            }
        }

        public static ulong BinaryUnsignedSatQSub(ulong op1, ulong op2)
        {
            ExecutionContext context = NativeInterface.GetContext();

            ulong sub = op1 - op2;

            if (op1 < op2)
            {
                context.Fpsr |= FPSR.Qc;

                return ulong.MinValue;
            }
            else
            {
                return sub;
            }
        }

        public static long BinarySignedSatQAcc(ulong op1, long op2)
        {
            ExecutionContext context = NativeInterface.GetContext();

            if (op1 <= (ulong)long.MaxValue)
            {
                // op1 from ulong.MinValue to (ulong)long.MaxValue
                // op2 from long.MinValue to long.MaxValue

                long add = (long)op1 + op2;

                if ((~op2 & add) < 0L)
                {
                    context.Fpsr |= FPSR.Qc;

                    return long.MaxValue;
                }
                else
                {
                    return add;
                }
            }
            else if (op2 >= 0L)
            {
                // op1 from (ulong)long.MaxValue + 1UL to ulong.MaxValue
                // op2 from (long)ulong.MinValue to long.MaxValue

                context.Fpsr |= FPSR.Qc;

                return long.MaxValue;
            }
            else
            {
                // op1 from (ulong)long.MaxValue + 1UL to ulong.MaxValue
                // op2 from long.MinValue to (long)ulong.MinValue - 1L

                ulong add = op1 + (ulong)op2;

                if (add > (ulong)long.MaxValue)
                {
                    context.Fpsr |= FPSR.Qc;

                    return long.MaxValue;
                }
                else
                {
                    return (long)add;
                }
            }
        }

        public static ulong BinaryUnsignedSatQAcc(long op1, ulong op2)
        {
            ExecutionContext context = NativeInterface.GetContext();

            if (op1 >= 0L)
            {
                // op1 from (long)ulong.MinValue to long.MaxValue
                // op2 from ulong.MinValue to ulong.MaxValue

                ulong add = (ulong)op1 + op2;

                if ((add < (ulong)op1) && (add < op2))
                {
                    context.Fpsr |= FPSR.Qc;

                    return ulong.MaxValue;
                }
                else
                {
                    return add;
                }
            }
            else if (op2 > (ulong)long.MaxValue)
            {
                // op1 from long.MinValue to (long)ulong.MinValue - 1L
                // op2 from (ulong)long.MaxValue + 1UL to ulong.MaxValue

                return (ulong)op1 + op2;
            }
            else
            {
                // op1 from long.MinValue to (long)ulong.MinValue - 1L
                // op2 from ulong.MinValue to (ulong)long.MaxValue

                long add = op1 + (long)op2;

                if (add < (long)ulong.MinValue)
                {
                    context.Fpsr |= FPSR.Qc;

                    return ulong.MinValue;
                }
                else
                {
                    return (ulong)add;
                }
            }
        }
#endregion

#region "Count"
        public static ulong CountLeadingSigns(ulong value, int size) // size is 8, 16, 32 or 64 (SIMD&FP or Base Inst.).
        {
            value ^= value >> 1;

            int highBit = size - 2;

            for (int bit = highBit; bit >= 0; bit--)
            {
                if (((int)(value >> bit) & 0b1) != 0)
                {
                    return (ulong)(highBit - bit);
                }
            }

            return (ulong)(size - 1);
        }

        private static ReadOnlySpan<byte> ClzNibbleTbl => new byte[] { 4, 3, 2, 2, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0 };

        public static ulong CountLeadingZeros(ulong value, int size) // size is 8, 16, 32 or 64 (SIMD&FP or Base Inst.).
        {
            if (value == 0ul)
            {
                return (ulong)size;
            }

            int nibbleIdx = size;
            int preCount, count = 0;

            do
            {
                nibbleIdx -= 4;
                preCount = ClzNibbleTbl[(int)(value >> nibbleIdx) & 0b1111];
                count += preCount;
            }
            while (preCount == 4);

            return (ulong)count;
        }
#endregion

#region "Table"
        public static V128 Tbl1(V128 vector, int bytes, V128 tb0)
        {
            return TblOrTbx(default, vector, bytes, tb0);
        }

        public static V128 Tbl2(V128 vector, int bytes, V128 tb0, V128 tb1)
        {
            return TblOrTbx(default, vector, bytes, tb0, tb1);
        }

        public static V128 Tbl3(V128 vector, int bytes, V128 tb0, V128 tb1, V128 tb2)
        {
            return TblOrTbx(default, vector, bytes, tb0, tb1, tb2);
        }

        public static V128 Tbl4(V128 vector, int bytes, V128 tb0, V128 tb1, V128 tb2, V128 tb3)
        {
            return TblOrTbx(default, vector, bytes, tb0, tb1, tb2, tb3);
        }

        public static V128 Tbx1(V128 dest, V128 vector, int bytes, V128 tb0)
        {
            return TblOrTbx(dest, vector, bytes, tb0);
        }

        public static V128 Tbx2(V128 dest, V128 vector, int bytes, V128 tb0, V128 tb1)
        {
            return TblOrTbx(dest, vector, bytes, tb0, tb1);
        }

        public static V128 Tbx3(V128 dest, V128 vector, int bytes, V128 tb0, V128 tb1, V128 tb2)
        {
            return TblOrTbx(dest, vector, bytes, tb0, tb1, tb2);
        }

        public static V128 Tbx4(V128 dest, V128 vector, int bytes, V128 tb0, V128 tb1, V128 tb2, V128 tb3)
        {
            return TblOrTbx(dest, vector, bytes, tb0, tb1, tb2, tb3);
        }

        private static V128 TblOrTbx(V128 dest, V128 vector, int bytes, params V128[] tb)
        {
            byte[] res = new byte[16];

            if (dest != default)
            {
                Buffer.BlockCopy(dest.ToArray(), 0, res, 0, bytes);
            }

            byte[] table = new byte[tb.Length * 16];

            for (byte index = 0; index < tb.Length; index++)
            {
                Buffer.BlockCopy(tb[index].ToArray(), 0, table, index * 16, 16);
            }

            byte[] v = vector.ToArray();

            for (byte index = 0; index < bytes; index++)
            {
                byte tblIndex = v[index];

                if (tblIndex < table.Length)
                {
                    res[index] = table[tblIndex];
                }
            }

            return new V128(res);
        }
#endregion

#region "Crc32"
        private const uint Crc32RevPoly  = 0xedb88320;
        private const uint Crc32cRevPoly = 0x82f63b78;

        public static uint Crc32b(uint crc, byte   value) => Crc32 (crc, Crc32RevPoly, value);
        public static uint Crc32h(uint crc, ushort value) => Crc32h(crc, Crc32RevPoly, value);
        public static uint Crc32w(uint crc, uint   value) => Crc32w(crc, Crc32RevPoly, value);
        public static uint Crc32x(uint crc, ulong  value) => Crc32x(crc, Crc32RevPoly, value);

        public static uint Crc32cb(uint crc, byte   value) => Crc32 (crc, Crc32cRevPoly, value);
        public static uint Crc32ch(uint crc, ushort value) => Crc32h(crc, Crc32cRevPoly, value);
        public static uint Crc32cw(uint crc, uint   value) => Crc32w(crc, Crc32cRevPoly, value);
        public static uint Crc32cx(uint crc, ulong  value) => Crc32x(crc, Crc32cRevPoly, value);

        private static uint Crc32h(uint crc, uint poly, ushort val)
        {
            crc = Crc32(crc, poly, (byte)(val >> 0));
            crc = Crc32(crc, poly, (byte)(val >> 8));

            return crc;
        }

        private static uint Crc32w(uint crc, uint poly, uint val)
        {
            crc = Crc32(crc, poly, (byte)(val >> 0));
            crc = Crc32(crc, poly, (byte)(val >> 8));
            crc = Crc32(crc, poly, (byte)(val >> 16));
            crc = Crc32(crc, poly, (byte)(val >> 24));

            return crc;
        }

        private static uint Crc32x(uint crc, uint poly, ulong val)
        {
            crc = Crc32(crc, poly, (byte)(val >> 0));
            crc = Crc32(crc, poly, (byte)(val >> 8));
            crc = Crc32(crc, poly, (byte)(val >> 16));
            crc = Crc32(crc, poly, (byte)(val >> 24));
            crc = Crc32(crc, poly, (byte)(val >> 32));
            crc = Crc32(crc, poly, (byte)(val >> 40));
            crc = Crc32(crc, poly, (byte)(val >> 48));
            crc = Crc32(crc, poly, (byte)(val >> 56));

            return crc;
        }

        private static uint Crc32(uint crc, uint poly, byte val)
        {
            crc ^= val;

            for (int bit = 7; bit >= 0; bit--)
            {
                uint mask = (uint)(-(int)(crc & 1));

                crc = (crc >> 1) ^ (poly & mask);
            }

            return crc;
        }
#endregion

#region "Aes"
        public static V128 Decrypt(V128 value, V128 roundKey)
        {
            return CryptoHelper.AesInvSubBytes(CryptoHelper.AesInvShiftRows(value ^ roundKey));
        }

        public static V128 Encrypt(V128 value, V128 roundKey)
        {
            return CryptoHelper.AesSubBytes(CryptoHelper.AesShiftRows(value ^ roundKey));
        }

        public static V128 InverseMixColumns(V128 value)
        {
            return CryptoHelper.AesInvMixColumns(value);
        }

        public static V128 MixColumns(V128 value)
        {
            return CryptoHelper.AesMixColumns(value);
        }
#endregion

#region "Sha1"
        public static V128 HashChoose(V128 hash_abcd, uint hash_e, V128 wk)
        {
            for (int e = 0; e <= 3; e++)
            {
                uint t = ShaChoose(hash_abcd.Extract<uint>(1),
                                   hash_abcd.Extract<uint>(2),
                                   hash_abcd.Extract<uint>(3));

                hash_e += Rol(hash_abcd.Extract<uint>(0), 5) + t + wk.Extract<uint>(e);

                t = Rol(hash_abcd.Extract<uint>(1), 30);

                hash_abcd.Insert(1, t);

                Rol32_160(ref hash_e, ref hash_abcd);
            }

            return hash_abcd;
        }

        public static uint FixedRotate(uint hash_e)
        {
            return hash_e.Rol(30);
        }

        public static V128 HashMajority(V128 hash_abcd, uint hash_e, V128 wk)
        {
            for (int e = 0; e <= 3; e++)
            {
                uint t = ShaMajority(hash_abcd.Extract<uint>(1),
                                     hash_abcd.Extract<uint>(2),
                                     hash_abcd.Extract<uint>(3));

                hash_e += Rol(hash_abcd.Extract<uint>(0), 5) + t + wk.Extract<uint>(e);

                t = Rol(hash_abcd.Extract<uint>(1), 30);

                hash_abcd.Insert(1, t);

                Rol32_160(ref hash_e, ref hash_abcd);
            }

            return hash_abcd;
        }

        public static V128 HashParity(V128 hash_abcd, uint hash_e, V128 wk)
        {
            for (int e = 0; e <= 3; e++)
            {
                uint t = ShaParity(hash_abcd.Extract<uint>(1),
                                   hash_abcd.Extract<uint>(2),
                                   hash_abcd.Extract<uint>(3));

                hash_e += Rol(hash_abcd.Extract<uint>(0), 5) + t + wk.Extract<uint>(e);

                t = Rol(hash_abcd.Extract<uint>(1), 30);

                hash_abcd.Insert(1, t);

                Rol32_160(ref hash_e, ref hash_abcd);
            }

            return hash_abcd;
        }

        public static V128 Sha1SchedulePart1(V128 w0_3, V128 w4_7, V128 w8_11)
        {
            ulong t2 = w4_7.Extract<ulong>(0);
            ulong t1 = w0_3.Extract<ulong>(1);

            V128 result = new V128(t1, t2);

            return result ^ (w0_3 ^ w8_11);
        }

        public static V128 Sha1SchedulePart2(V128 tw0_3, V128 w12_15)
        {
            V128 t = tw0_3 ^ (w12_15 >> 32);

            uint tE0 = t.Extract<uint>(0);
            uint tE1 = t.Extract<uint>(1);
            uint tE2 = t.Extract<uint>(2);
            uint tE3 = t.Extract<uint>(3);

            return new V128(tE0.Rol(1), tE1.Rol(1), tE2.Rol(1), tE3.Rol(1) ^ tE0.Rol(2));
        }

        private static void Rol32_160(ref uint y, ref V128 x)
        {
            uint xE3 = x.Extract<uint>(3);

            x <<= 32;
            x.Insert(0, y);

            y = xE3;
        }

        private static uint ShaChoose(uint x, uint y, uint z)
        {
            return ((y ^ z) & x) ^ z;
        }

        private static uint ShaMajority(uint x, uint y, uint z)
        {
            return (x & y) | ((x | y) & z);
        }

        private static uint ShaParity(uint x, uint y, uint z)
        {
            return x ^ y ^ z;
        }

        private static uint Rol(this uint value, int count)
        {
            return (value << count) | (value >> (32 - count));
        }
#endregion

#region "Sha256"
        public static V128 HashLower(V128 hash_abcd, V128 hash_efgh, V128 wk)
        {
            return Sha256Hash(hash_abcd, hash_efgh, wk, part1: true);
        }

        public static V128 HashUpper(V128 hash_efgh, V128 hash_abcd, V128 wk)
        {
            return Sha256Hash(hash_abcd, hash_efgh, wk, part1: false);
        }

        public static V128 Sha256SchedulePart1(V128 w0_3, V128 w4_7)
        {
            V128 result = new V128();

            for (int e = 0; e <= 3; e++)
            {
                uint elt = (e <= 2 ? w0_3 : w4_7).Extract<uint>(e <= 2 ? e + 1 : 0);

                elt = elt.Ror(7) ^ elt.Ror(18) ^ elt.Lsr(3);

                elt += w0_3.Extract<uint>(e);

                result.Insert(e, elt);
            }

            return result;
        }

        public static V128 Sha256SchedulePart2(V128 w0_3, V128 w8_11, V128 w12_15)
        {
            V128 result = new V128();

            ulong t1 = w12_15.Extract<ulong>(1);

            for (int e = 0; e <= 1; e++)
            {
                uint elt = t1.ULongPart(e);

                elt = elt.Ror(17) ^ elt.Ror(19) ^ elt.Lsr(10);

                elt += w0_3.Extract<uint>(e) + w8_11.Extract<uint>(e + 1);

                result.Insert(e, elt);
            }

            t1 = result.Extract<ulong>(0);

            for (int e = 2; e <= 3; e++)
            {
                uint elt = t1.ULongPart(e - 2);

                elt = elt.Ror(17) ^ elt.Ror(19) ^ elt.Lsr(10);

                elt += w0_3.Extract<uint>(e) + (e == 2 ? w8_11 : w12_15).Extract<uint>(e == 2 ? 3 : 0);

                result.Insert(e, elt);
            }

            return result;
        }

        private static V128 Sha256Hash(V128 x, V128 y, V128 w, bool part1)
        {
            for (int e = 0; e <= 3; e++)
            {
                uint chs = ShaChoose(y.Extract<uint>(0),
                                     y.Extract<uint>(1),
                                     y.Extract<uint>(2));

                uint maj = ShaMajority(x.Extract<uint>(0),
                                       x.Extract<uint>(1),
                                       x.Extract<uint>(2));

                uint t1 = y.Extract<uint>(3) + ShaHashSigma1(y.Extract<uint>(0)) + chs + w.Extract<uint>(e);

                uint t2 = t1 + x.Extract<uint>(3);

                x.Insert(3, t2);

                t2 = t1 + ShaHashSigma0(x.Extract<uint>(0)) + maj;

                y.Insert(3, t2);

                Rol32_256(ref y, ref x);
            }

            return part1 ? x : y;
        }

        private static void Rol32_256(ref V128 y, ref V128 x)
        {
            uint yE3 = y.Extract<uint>(3);
            uint xE3 = x.Extract<uint>(3);

            y <<= 32;
            x <<= 32;

            y.Insert(0, xE3);
            x.Insert(0, yE3);
        }

        private static uint ShaHashSigma0(uint x)
        {
            return x.Ror(2) ^ x.Ror(13) ^ x.Ror(22);
        }

        private static uint ShaHashSigma1(uint x)
        {
            return x.Ror(6) ^ x.Ror(11) ^ x.Ror(25);
        }

        private static uint Ror(this uint value, int count)
        {
            return (value >> count) | (value << (32 - count));
        }

        private static uint Lsr(this uint value, int count)
        {
            return value >> count;
        }

        private static uint ULongPart(this ulong value, int part)
        {
            return part == 0
                ? (uint)(value & 0xFFFFFFFFUL)
                : (uint)(value >> 32);
        }
#endregion

        public static V128 PolynomialMult64_128(ulong op1, ulong op2)
        {
            V128 result = V128.Zero;

            V128 op2_128 = new V128(op2, 0);

            for (int i = 0; i < 64; i++)
            {
                if (((op1 >> i) & 1) == 1)
                {
                    result ^= op2_128 << i;
                }
            }

            return result;
        }
    }
}
