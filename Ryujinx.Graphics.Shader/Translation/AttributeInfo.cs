﻿using Ryujinx.Common;
using System.Collections.Generic;

namespace Ryujinx.Graphics.Shader.Translation
{
    struct AttributeInfo
    {
        private static readonly Dictionary<int, AttributeInfo> BuiltInAttributes = new Dictionary<int, AttributeInfo>()
        {
            { AttributeConsts.Layer,         new AttributeInfo(AttributeConsts.Layer,         0, 1, AggregateType.S32) },
            { AttributeConsts.ViewportIndex, new AttributeInfo(AttributeConsts.ViewportIndex, 0, 1, AggregateType.S32) },
            { AttributeConsts.PointSize,     new AttributeInfo(AttributeConsts.PointSize,     0, 1, AggregateType.FP32) },
            { AttributeConsts.PositionX,     new AttributeInfo(AttributeConsts.PositionX,     0, 4, AggregateType.Vector | AggregateType.FP32) },
            { AttributeConsts.PositionY,     new AttributeInfo(AttributeConsts.PositionX,     1, 4, AggregateType.Vector | AggregateType.FP32) },
            { AttributeConsts.PositionZ,     new AttributeInfo(AttributeConsts.PositionX,     2, 4, AggregateType.Vector | AggregateType.FP32) },
            { AttributeConsts.PositionW,     new AttributeInfo(AttributeConsts.PositionX,     3, 4, AggregateType.Vector | AggregateType.FP32) },
            { AttributeConsts.ClipDistance0, new AttributeInfo(AttributeConsts.ClipDistance0, 0, 8, AggregateType.Array  | AggregateType.FP32) },
            { AttributeConsts.ClipDistance1, new AttributeInfo(AttributeConsts.ClipDistance0, 1, 8, AggregateType.Array  | AggregateType.FP32) },
            { AttributeConsts.ClipDistance2, new AttributeInfo(AttributeConsts.ClipDistance0, 2, 8, AggregateType.Array  | AggregateType.FP32) },
            { AttributeConsts.ClipDistance3, new AttributeInfo(AttributeConsts.ClipDistance0, 3, 8, AggregateType.Array  | AggregateType.FP32) },
            { AttributeConsts.ClipDistance4, new AttributeInfo(AttributeConsts.ClipDistance0, 4, 8, AggregateType.Array  | AggregateType.FP32) },
            { AttributeConsts.ClipDistance5, new AttributeInfo(AttributeConsts.ClipDistance0, 5, 8, AggregateType.Array  | AggregateType.FP32) },
            { AttributeConsts.ClipDistance6, new AttributeInfo(AttributeConsts.ClipDistance0, 6, 8, AggregateType.Array  | AggregateType.FP32) },
            { AttributeConsts.ClipDistance7, new AttributeInfo(AttributeConsts.ClipDistance0, 7, 8, AggregateType.Array  | AggregateType.FP32) },
            { AttributeConsts.PointCoordX,   new AttributeInfo(AttributeConsts.PointCoordX,   0, 2, AggregateType.Vector | AggregateType.FP32) },
            { AttributeConsts.PointCoordY,   new AttributeInfo(AttributeConsts.PointCoordX,   1, 2, AggregateType.Vector | AggregateType.FP32) },
            { AttributeConsts.TessCoordX,    new AttributeInfo(AttributeConsts.TessCoordX,    0, 2, AggregateType.Vector | AggregateType.FP32) },
            { AttributeConsts.TessCoordY,    new AttributeInfo(AttributeConsts.TessCoordX,    1, 2, AggregateType.Vector | AggregateType.FP32) },
            { AttributeConsts.InstanceId,    new AttributeInfo(AttributeConsts.InstanceId,    0, 1, AggregateType.S32) },
            { AttributeConsts.VertexId,      new AttributeInfo(AttributeConsts.VertexId,      0, 1, AggregateType.S32) },
            { AttributeConsts.FrontFacing,   new AttributeInfo(AttributeConsts.FrontFacing,   0, 1, AggregateType.Bool) },

            // Special.
            { AttributeConsts.FragmentOutputDepth, new AttributeInfo(AttributeConsts.FragmentOutputDepth, 0, 1, AggregateType.FP32) },
            { AttributeConsts.ThreadKill,          new AttributeInfo(AttributeConsts.ThreadKill,          0, 1, AggregateType.Bool) },
            { AttributeConsts.ThreadIdX,           new AttributeInfo(AttributeConsts.ThreadIdX,           0, 3, AggregateType.Vector | AggregateType.U32) },
            { AttributeConsts.ThreadIdY,           new AttributeInfo(AttributeConsts.ThreadIdX,           1, 3, AggregateType.Vector | AggregateType.U32) },
            { AttributeConsts.ThreadIdZ,           new AttributeInfo(AttributeConsts.ThreadIdX,           2, 3, AggregateType.Vector | AggregateType.U32) },
            { AttributeConsts.CtaIdX,              new AttributeInfo(AttributeConsts.CtaIdX,              0, 3, AggregateType.Vector | AggregateType.U32) },
            { AttributeConsts.CtaIdY,              new AttributeInfo(AttributeConsts.CtaIdX,              1, 3, AggregateType.Vector | AggregateType.U32) },
            { AttributeConsts.CtaIdZ,              new AttributeInfo(AttributeConsts.CtaIdX,              2, 3, AggregateType.Vector | AggregateType.U32) },
            { AttributeConsts.LaneId,              new AttributeInfo(AttributeConsts.LaneId,              0, 1, AggregateType.U32) },
            { AttributeConsts.EqMask,              new AttributeInfo(AttributeConsts.EqMask,              0, 4, AggregateType.Vector | AggregateType.U32) },
            { AttributeConsts.GeMask,              new AttributeInfo(AttributeConsts.GeMask,              0, 4, AggregateType.Vector | AggregateType.U32) },
            { AttributeConsts.GtMask,              new AttributeInfo(AttributeConsts.GtMask,              0, 4, AggregateType.Vector | AggregateType.U32) },
            { AttributeConsts.LeMask,              new AttributeInfo(AttributeConsts.LeMask,              0, 4, AggregateType.Vector | AggregateType.U32) },
            { AttributeConsts.LtMask,              new AttributeInfo(AttributeConsts.LtMask,              0, 4, AggregateType.Vector | AggregateType.U32) },
        };

        public int BaseValue { get; }
        public int Value { get; }
        public int Length { get; }
        public AggregateType Type { get; }
        public bool IsBuiltin { get; }
        public bool IsValid => Type != AggregateType.Invalid;

        public AttributeInfo(int baseValue, int index, int length, AggregateType type, bool isBuiltin = true)
        {
            BaseValue = baseValue;
            Value = baseValue + index * 4;
            Length = length;
            Type = type;
            IsBuiltin = isBuiltin;
        }

        public int GetInnermostIndex()
        {
            return (Value - BaseValue) / 4;
        }

        public static AttributeInfo From(ShaderConfig config, int value)
        {
            value &= ~3;

            if (value >= AttributeConsts.UserAttributeBase && value < AttributeConsts.UserAttributeEnd)
            {
                int location = (value - AttributeConsts.UserAttributeBase) / 16;
                var elemType = config.GpuAccessor.QueryAttributeType(location) switch
                {
                    AttributeType.Sint => AggregateType.S32,
                    AttributeType.Uint => AggregateType.U32,
                    _ => AggregateType.FP32
                };

                return new AttributeInfo(value & ~0xf, (value >> 2) & 3, 4, AggregateType.Vector | elemType, false);
            }
            else if (value >= AttributeConsts.FragmentOutputColorBase && value < AttributeConsts.FragmentOutputColorEnd)
            {
                return new AttributeInfo(value & ~0xf, (value >> 2) & 3, 4, AggregateType.Vector | AggregateType.FP32, false);
            }
            else if (BuiltInAttributes.TryGetValue(value, out AttributeInfo info))
            {
                return info;
            }

            return new AttributeInfo(value, 0, 0, AggregateType.Invalid);
        }
    }
}
