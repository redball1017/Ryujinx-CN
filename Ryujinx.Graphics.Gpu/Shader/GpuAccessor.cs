﻿using Ryujinx.Common.Logging;
using Ryujinx.Graphics.GAL;
using Ryujinx.Graphics.Shader;
using Ryujinx.Graphics.Shader.Translation;
using System;
using System.Runtime.InteropServices;

namespace Ryujinx.Graphics.Gpu.Shader
{
    /// <summary>
    /// Represents a GPU state and memory accessor.
    /// </summary>
    class GpuAccessor : TextureDescriptorCapableGpuAccessor, IGpuAccessor
    {
        private readonly GpuChannel _channel;
        private readonly GpuAccessorState _state;
        private readonly AttributeType[] _attributeTypes;
        private readonly int _stageIndex;
        private readonly bool _compute;
        private readonly int _localSizeX;
        private readonly int _localSizeY;
        private readonly int _localSizeZ;
        private readonly int _localMemorySize;
        private readonly int _sharedMemorySize;
        private readonly bool _isVulkan;

        public int Cb1DataSize { get; private set; }

        /// <summary>
        /// Creates a new instance of the GPU state accessor for graphics shader translation.
        /// </summary>
        /// <param name="context">GPU context</param>
        /// <param name="channel">GPU channel</param>
        /// <param name="state">Current GPU state</param>
        /// <param name="attributeTypes">Type of the vertex attributes consumed by the shader</param>
        /// <param name="stageIndex">Graphics shader stage index (0 = Vertex, 4 = Fragment)</param>
        public GpuAccessor(
            GpuContext context,
            GpuChannel channel,
            GpuAccessorState state,
            AttributeType[] attributeTypes,
            int stageIndex) : base(context)
        {
            _isVulkan = context.Capabilities.Api == TargetApi.Vulkan;
            _channel = channel;
            _state = state;
            _attributeTypes = attributeTypes;
            _stageIndex = stageIndex;
        }

        /// <summary>
        /// Creates a new instance of the GPU state accessor for compute shader translation.
        /// </summary>
        /// <param name="context">GPU context</param>
        /// <param name="channel">GPU channel</param>
        /// <param name="state">Current GPU state</param>
        /// <param name="localSizeX">Local group size X of the compute shader</param>
        /// <param name="localSizeY">Local group size Y of the compute shader</param>
        /// <param name="localSizeZ">Local group size Z of the compute shader</param>
        /// <param name="localMemorySize">Local memory size of the compute shader</param>
        /// <param name="sharedMemorySize">Shared memory size of the compute shader</param>
        public GpuAccessor(
            GpuContext context,
            GpuChannel channel,
            GpuAccessorState state,
            int localSizeX,
            int localSizeY,
            int localSizeZ,
            int localMemorySize,
            int sharedMemorySize) : base(context)
        {
            _channel = channel;
            _state = state;
            _compute = true;
            _localSizeX = localSizeX;
            _localSizeY = localSizeY;
            _localSizeZ = localSizeZ;
            _localMemorySize = localMemorySize;
            _sharedMemorySize = sharedMemorySize;
        }

        /// <summary>
        /// Reads data from the constant buffer 1.
        /// </summary>
        /// <param name="offset">Offset in bytes to read from</param>
        /// <returns>Value at the given offset</returns>
        public uint ConstantBuffer1Read(int offset)
        {
            if (Cb1DataSize < offset + 4)
            {
                Cb1DataSize = offset + 4;
            }

            ulong baseAddress = _compute
                ? _channel.BufferManager.GetComputeUniformBufferAddress(1)
                : _channel.BufferManager.GetGraphicsUniformBufferAddress(_stageIndex, 1);

            return _channel.MemoryManager.Physical.Read<uint>(baseAddress + (ulong)offset);
        }

        /// <summary>
        /// Prints a log message.
        /// </summary>
        /// <param name="message">Message to print</param>
        public void Log(string message)
        {
            Logger.Warning?.Print(LogClass.Gpu, $"Shader translator: {message}");
        }

        /// <summary>
        /// Gets a span of the specified memory location, containing shader code.
        /// </summary>
        /// <param name="address">GPU virtual address of the data</param>
        /// <param name="minimumSize">Minimum size that the returned span may have</param>
        /// <returns>Span of the memory location</returns>
        public override ReadOnlySpan<ulong> GetCode(ulong address, int minimumSize)
        {
            int size = Math.Max(minimumSize, 0x1000 - (int)(address & 0xfff));
            return MemoryMarshal.Cast<byte, ulong>(_channel.MemoryManager.GetSpan(address, size));
        }

        /// <summary>
        /// Gets the comparison used to decide if the fragment should be discarded depending on the alpha value.
        /// </summary>
        /// <returns>Alpha test comparison</returns>
        public AlphaTestOp QueryAlphaTestCompare()
        {
            if (!_isVulkan || !_state.AlphaTestEnable)
            {
                return AlphaTestOp.Always;
            }

            return _state.AlphaTestCompare switch
            {
                CompareOp.Never or CompareOp.NeverGl => AlphaTestOp.Never,
                CompareOp.Less or CompareOp.LessGl => AlphaTestOp.Less,
                CompareOp.Equal or CompareOp.EqualGl => AlphaTestOp.Equal,
                CompareOp.LessOrEqual or CompareOp.LessOrEqualGl => AlphaTestOp.LessOrEqual,
                CompareOp.Greater or CompareOp.GreaterGl => AlphaTestOp.Greater,
                CompareOp.NotEqual or CompareOp.NotEqualGl => AlphaTestOp.NotEqual,
                CompareOp.GreaterOrEqual or CompareOp.GreaterOrEqualGl => AlphaTestOp.GreaterOrEqual,
                _ => AlphaTestOp.Always
            };
        }

        /// <summary>
        /// Gets the reference value to be compared with the fragment alpha when alpha test is enabled.
        /// </summary>
        /// <returns>Alpha test reference value</returns>
        public float QueryAlphaTestReference()
        {
            return _state.AlphaTestReference;
        }

        /// <summary>
        /// Gets the type of a vertex attribute at the given location.
        /// </summary>
        /// <param name="address">User attribute location</param>
        /// <returns>Type of the attribute</returns>
        public AttributeType QueryAttributeType(int location)
        {
            if (_attributeTypes != null)
            {
                return _attributeTypes[location];
            }

            return AttributeType.Float;
        }

        /// <summary>
        /// Queries Local Size X for compute shaders.
        /// </summary>
        /// <returns>Local Size X</returns>
        public int QueryComputeLocalSizeX() => _localSizeX;

        /// <summary>
        /// Queries Local Size Y for compute shaders.
        /// </summary>
        /// <returns>Local Size Y</returns>
        public int QueryComputeLocalSizeY() => _localSizeY;

        /// <summary>
        /// Queries Local Size Z for compute shaders.
        /// </summary>
        /// <returns>Local Size Z</returns>
        public int QueryComputeLocalSizeZ() => _localSizeZ;

        /// <summary>
        /// Queries Local Memory size in bytes for compute shaders.
        /// </summary>
        /// <returns>Local Memory size in bytes</returns>
        public int QueryComputeLocalMemorySize() => _localMemorySize;

        /// <summary>
        /// Queries Shared Memory size in bytes for compute shaders.
        /// </summary>
        /// <returns>Shared Memory size in bytes</returns>
        public int QueryComputeSharedMemorySize() => _sharedMemorySize;

        /// <summary>
        /// Queries Constant Buffer usage information.
        /// </summary>
        /// <returns>A mask where each bit set indicates a bound constant buffer</returns>
        public uint QueryConstantBufferUse()
        {
            return _compute
                ? _channel.BufferManager.GetComputeUniformBufferUseMask()
                : _channel.BufferManager.GetGraphicsUniformBufferUseMask(_stageIndex);
        }

        /// <summary>
        /// Queries current primitive topology for geometry shaders.
        /// </summary>
        /// <returns>Current primitive topology</returns>
        public InputTopology QueryPrimitiveTopology()
        {
            return _state.Topology switch
            {
                PrimitiveTopology.Points => InputTopology.Points,
                PrimitiveTopology.Lines or
                PrimitiveTopology.LineLoop or
                PrimitiveTopology.LineStrip => InputTopology.Lines,
                PrimitiveTopology.LinesAdjacency or
                PrimitiveTopology.LineStripAdjacency => InputTopology.LinesAdjacency,
                PrimitiveTopology.Triangles or
                PrimitiveTopology.TriangleStrip or
                PrimitiveTopology.TriangleFan => InputTopology.Triangles,
                PrimitiveTopology.TrianglesAdjacency or
                PrimitiveTopology.TriangleStripAdjacency => InputTopology.TrianglesAdjacency,
                PrimitiveTopology.Patches => _state.TessellationMode.UnpackPatchType() == TessPatchType.Isolines
                    ? InputTopology.Lines
                    : InputTopology.Triangles,
                _ => InputTopology.Points
            };
        }

        public bool QueryProgramPointSize()
        {
            return _state.ProgramPointSizeEnable;
        }

        public float QueryPointSize()
        {
            return _state.PointSize;
        }

        /// <summary>
        /// Queries the tessellation evaluation shader primitive winding order.
        /// </summary>
        /// <returns>True if the primitive winding order is clockwise, false if counter-clockwise</returns>
        public bool QueryTessCw() => _state.TessellationMode.UnpackCw();

        /// <summary>
        /// Queries the tessellation evaluation shader abstract patch type.
        /// </summary>
        /// <returns>Abstract patch type</returns>
        public TessPatchType QueryTessPatchType() => _state.TessellationMode.UnpackPatchType();

        /// <summary>
        /// Queries the tessellation evaluation shader spacing between tessellated vertices of the patch.
        /// </summary>
        /// <returns>Spacing between tessellated vertices of the patch</returns>
        public TessSpacing QueryTessSpacing() => _state.TessellationMode.UnpackSpacing();

        /// <summary>
        /// Gets the texture descriptor for a given texture on the pool.
        /// </summary>
        /// <param name="handle">Index of the texture (this is the word offset of the handle in the constant buffer)</param>
        /// <param name="cbufSlot">Constant buffer slot for the texture handle</param>
        /// <returns>Texture descriptor</returns>
        public override Image.ITextureDescriptor GetTextureDescriptor(int handle, int cbufSlot)
        {
            if (_compute)
            {
                return _channel.TextureManager.GetComputeTextureDescriptor(
                    _state.TexturePoolGpuVa,
                    _state.TextureBufferIndex,
                    _state.TexturePoolMaximumId,
                    handle,
                    cbufSlot);
            }
            else
            {
                return _channel.TextureManager.GetGraphicsTextureDescriptor(
                    _state.TexturePoolGpuVa,
                    _state.TextureBufferIndex,
                    _state.TexturePoolMaximumId,
                    _stageIndex,
                    handle,
                    cbufSlot);
            }
        }

        public bool QueryTransformDepthMinusOneToOne()
        {
            return _state.DepthMode;
        }

        /// <summary>
        /// Queries transform feedback enable state.
        /// </summary>
        /// <returns>True if the shader uses transform feedback, false otherwise</returns>
        public bool QueryTransformFeedbackEnabled()
        {
            return _state.TransformFeedbackDescriptors != null;
        }

        /// <summary>
        /// Queries the varying locations that should be written to the transform feedback buffer.
        /// </summary>
        /// <param name="bufferIndex">Index of the transform feedback buffer</param>
        /// <returns>Varying locations for the specified buffer</returns>
        public ReadOnlySpan<byte> QueryTransformFeedbackVaryingLocations(int bufferIndex)
        {
            return _state.TransformFeedbackDescriptors[bufferIndex].VaryingLocations;
        }

        /// <summary>
        /// Queries the stride (in bytes) of the per vertex data written into the transform feedback buffer.
        /// </summary>
        /// <param name="bufferIndex">Index of the transform feedback buffer</param>
        /// <returns>Stride for the specified buffer</returns>
        public int QueryTransformFeedbackStride(int bufferIndex)
        {
            return _state.TransformFeedbackDescriptors[bufferIndex].Stride;
        }

        /// <summary>
        /// Queries if host state forces early depth testing.
        /// </summary>
        /// <returns>True if early depth testing is forced</returns>
        public bool QueryEarlyZForce()
        {
            return _state.EarlyZForce;
        }
    }
}
