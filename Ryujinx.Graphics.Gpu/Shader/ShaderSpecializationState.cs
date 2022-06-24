using Ryujinx.Common.Memory;
using Ryujinx.Graphics.Gpu.Image;
using Ryujinx.Graphics.Gpu.Memory;
using Ryujinx.Graphics.Gpu.Shader.DiskCache;
using Ryujinx.Graphics.Shader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ryujinx.Graphics.Gpu.Shader
{
    class ShaderSpecializationState
    {
        private const uint ComsMagic = (byte)'C' | ((byte)'O' << 8) | ((byte)'M' << 16) | ((byte)'S' << 24);
        private const uint GfxsMagic = (byte)'G' | ((byte)'F' << 8) | ((byte)'X' << 16) | ((byte)'S' << 24);
        private const uint TfbdMagic = (byte)'T' | ((byte)'F' << 8) | ((byte)'B' << 16) | ((byte)'D' << 24);
        private const uint TexkMagic = (byte)'T' | ((byte)'E' << 8) | ((byte)'X' << 16) | ((byte)'K' << 24);
        private const uint TexsMagic = (byte)'T' | ((byte)'E' << 8) | ((byte)'X' << 16) | ((byte)'S' << 24);

        /// <summary>
        /// Flags indicating GPU state that is used by the shader.
        /// </summary>
        [Flags]
        private enum QueriedStateFlags
        {
            EarlyZForce = 1 << 0,
            PrimitiveTopology = 1 << 1,
            TessellationMode = 1 << 2,
            TransformFeedback = 1 << 3
        }

        private QueriedStateFlags _queriedState;
        private bool _compute;
        private byte _constantBufferUsePerStage;

        /// <summary>
        /// Compute engine state.
        /// </summary>
        public GpuChannelComputeState ComputeState;

        /// <summary>
        /// 3D engine state.
        /// </summary>
        public GpuChannelGraphicsState GraphicsState;

        /// <summary>
        /// Contant buffers bound at the time the shader was compiled, per stage.
        /// </summary>
        public Array5<uint> ConstantBufferUse;

        /// <summary>
        /// Transform feedback buffers active at the time the shader was compiled.
        /// </summary>
        public TransformFeedbackDescriptor[] TransformFeedbackDescriptors;

        /// <summary>
        /// Flags indicating texture state that is used by the shader.
        /// </summary>
        [Flags]
        private enum QueriedTextureStateFlags
        {
            TextureFormat = 1 << 0,
            SamplerType = 1 << 1,
            CoordNormalized = 1 << 2
        }

        /// <summary>
        /// Reference type wrapping a value.
        /// </summary>
        private class Box<T>
        {
            /// <summary>
            /// Wrapped value.
            /// </summary>
            public T Value;
        }

        /// <summary>
        /// State of a texture or image that is accessed by the shader.
        /// </summary>
        private struct TextureSpecializationState
        {
            // New fields should be added to the end of the struct to keep disk shader cache compatibility.

            /// <summary>
            /// Flags indicating which state of the texture the shader depends on.
            /// </summary>
            public QueriedTextureStateFlags QueriedFlags;

            /// <summary>
            /// Encoded texture format value.
            /// </summary>
            public uint Format;

            /// <summary>
            /// True if the texture format is sRGB, false otherwise.
            /// </summary>
            public bool FormatSrgb;

            /// <summary>
            /// Texture target.
            /// </summary>
            public Image.TextureTarget TextureTarget;

            /// <summary>
            /// Indicates if the coordinates used to sample the texture are normalized or not (0.0..1.0 or 0..Width/Height).
            /// </summary>
            public bool CoordNormalized;
        }

        /// <summary>
        /// Texture binding information, used to identify each texture accessed by the shader.
        /// </summary>
        private struct TextureKey : IEquatable<TextureKey>
        {
            // New fields should be added to the end of the struct to keep disk shader cache compatibility.

            /// <summary>
            /// Shader stage where the texture is used.
            /// </summary>
            public readonly int StageIndex;

            /// <summary>
            /// Texture handle offset in words on the texture buffer.
            /// </summary>
            public readonly int Handle;

            /// <summary>
            /// Constant buffer slot of the texture buffer (-1 to use the texture buffer index GPU register).
            /// </summary>
            public readonly int CbufSlot;

            /// <summary>
            /// Creates a new texture key.
            /// </summary>
            /// <param name="stageIndex">Shader stage where the texture is used</param>
            /// <param name="handle">Texture handle offset in words on the texture buffer</param>
            /// <param name="cbufSlot">Constant buffer slot of the texture buffer (-1 to use the texture buffer index GPU register)</param>
            public TextureKey(int stageIndex, int handle, int cbufSlot)
            {
                StageIndex = stageIndex;
                Handle = handle;
                CbufSlot = cbufSlot;
            }

            public override bool Equals(object obj)
            {
                return obj is TextureKey textureKey && Equals(textureKey);
            }

            public bool Equals(TextureKey other)
            {
                return StageIndex == other.StageIndex && Handle == other.Handle && CbufSlot == other.CbufSlot;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(StageIndex, Handle, CbufSlot);
            }
        }

        private readonly Dictionary<TextureKey, Box<TextureSpecializationState>> _textureSpecialization;
        private KeyValuePair<TextureKey, Box<TextureSpecializationState>>[] _allTextures;
        private Box<TextureSpecializationState>[][] _textureByBinding;
        private Box<TextureSpecializationState>[][] _imageByBinding;

        /// <summary>
        /// Creates a new instance of the shader specialization state.
        /// </summary>
        private ShaderSpecializationState()
        {
            _textureSpecialization = new Dictionary<TextureKey, Box<TextureSpecializationState>>();
        }

        /// <summary>
        /// Creates a new instance of the shader specialization state.
        /// </summary>
        /// <param name="state">Current compute engine state</param>
        public ShaderSpecializationState(GpuChannelComputeState state) : this()
        {
            ComputeState = state;
            _compute = true;
        }

        /// <summary>
        /// Creates a new instance of the shader specialization state.
        /// </summary>
        /// <param name="state">Current 3D engine state</param>
        /// <param name="descriptors">Optional transform feedback buffers in use, if any</param>
        public ShaderSpecializationState(GpuChannelGraphicsState state, TransformFeedbackDescriptor[] descriptors) : this()
        {
            GraphicsState = state;
            _compute = false;

            if (descriptors != null)
            {
                TransformFeedbackDescriptors = descriptors;
                _queriedState |= QueriedStateFlags.TransformFeedback;
            }
        }

        /// <summary>
        /// Prepare the shader specialization state for quick binding lookups.
        /// </summary>
        /// <param name="stages">The shader stages</param>
        public void Prepare(CachedShaderStage[] stages)
        {
            _allTextures = _textureSpecialization.ToArray();

            _textureByBinding = new Box<TextureSpecializationState>[stages.Length][];
            _imageByBinding = new Box<TextureSpecializationState>[stages.Length][];

            for (int i = 0; i < stages.Length; i++)
            {
                CachedShaderStage stage = stages[i];
                if (stage?.Info != null)
                {
                    var textures = stage.Info.Textures;
                    var images = stage.Info.Images;

                    var texBindings = new Box<TextureSpecializationState>[textures.Count];
                    var imageBindings = new Box<TextureSpecializationState>[images.Count];

                    int stageIndex = Math.Max(i - 1, 0); // Don't count VertexA for looking up spec state. No-Op for compute.

                    for (int j = 0; j < textures.Count; j++)
                    {
                        var texture = textures[j];
                        texBindings[j] = GetTextureSpecState(stageIndex, texture.HandleIndex, texture.CbufSlot);
                    }

                    for (int j = 0; j < images.Count; j++)
                    {
                        var image = images[j];
                        imageBindings[j] = GetTextureSpecState(stageIndex, image.HandleIndex, image.CbufSlot);
                    }

                    _textureByBinding[i] = texBindings;
                    _imageByBinding[i] = imageBindings;
                }
            }
        }

        /// <summary>
        /// Indicates that the shader accesses the early Z force state.
        /// </summary>
        public void RecordEarlyZForce()
        {
            _queriedState |= QueriedStateFlags.EarlyZForce;
        }

        /// <summary>
        /// Indicates that the shader accesses the primitive topology state.
        /// </summary>
        public void RecordPrimitiveTopology()
        {
            _queriedState |= QueriedStateFlags.PrimitiveTopology;
        }

        /// <summary>
        /// Indicates that the shader accesses the tessellation mode state.
        /// </summary>
        public void RecordTessellationMode()
        {
            _queriedState |= QueriedStateFlags.TessellationMode;
        }

        /// <summary>
        /// Indicates that the shader accesses the constant buffer use state.
        /// </summary>
        /// <param name="stageIndex">Shader stage index</param>
        /// <param name="useMask">Mask indicating the constant buffers bound at the time of the shader compilation</param>
        public void RecordConstantBufferUse(int stageIndex, uint useMask)
        {
            ConstantBufferUse[stageIndex] = useMask;
            _constantBufferUsePerStage |= (byte)(1 << stageIndex);
        }

        /// <summary>
        /// Indicates that a given texture is accessed by the shader.
        /// </summary>
        /// <param name="stageIndex">Shader stage where the texture is used</param>
        /// <param name="handle">Offset in words of the texture handle on the texture buffer</param>
        /// <param name="cbufSlot">Slot of the texture buffer constant buffer</param>
        /// <param name="descriptor">Descriptor of the texture</param>
        public void RegisterTexture(int stageIndex, int handle, int cbufSlot, Image.TextureDescriptor descriptor)
        {
            Box<TextureSpecializationState> state = GetOrCreateTextureSpecState(stageIndex, handle, cbufSlot);
            state.Value.Format = descriptor.UnpackFormat();
            state.Value.FormatSrgb = descriptor.UnpackSrgb();
            state.Value.TextureTarget = descriptor.UnpackTextureTarget();
            state.Value.CoordNormalized = descriptor.UnpackTextureCoordNormalized();
        }

        /// <summary>
        /// Indicates that a given texture is accessed by the shader.
        /// </summary>
        /// <param name="stageIndex">Shader stage where the texture is used</param>
        /// <param name="handle">Offset in words of the texture handle on the texture buffer</param>
        /// <param name="cbufSlot">Slot of the texture buffer constant buffer</param>
        /// <param name="format">Maxwell texture format value</param>
        /// <param name="formatSrgb">Whenever the texture format is a sRGB format</param>
        /// <param name="target">Texture target type</param>
        /// <param name="coordNormalized">Whenever the texture coordinates used on the shader are considered normalized</param>
        public void RegisterTexture(
            int stageIndex,
            int handle,
            int cbufSlot,
            uint format,
            bool formatSrgb,
            Image.TextureTarget target,
            bool coordNormalized)
        {
            Box<TextureSpecializationState> state = GetOrCreateTextureSpecState(stageIndex, handle, cbufSlot);
            state.Value.Format = format;
            state.Value.FormatSrgb = formatSrgb;
            state.Value.TextureTarget = target;
            state.Value.CoordNormalized = coordNormalized;
        }

        /// <summary>
        /// Indicates that the format of a given texture was used during the shader translation process.
        /// </summary>
        /// <param name="stageIndex">Shader stage where the texture is used</param>
        /// <param name="handle">Offset in words of the texture handle on the texture buffer</param>
        /// <param name="cbufSlot">Slot of the texture buffer constant buffer</param>
        public void RecordTextureFormat(int stageIndex, int handle, int cbufSlot)
        {
            Box<TextureSpecializationState> state = GetOrCreateTextureSpecState(stageIndex, handle, cbufSlot);
            state.Value.QueriedFlags |= QueriedTextureStateFlags.TextureFormat;
        }

        /// <summary>
        /// Indicates that the target of a given texture was used during the shader translation process.
        /// </summary>
        /// <param name="stageIndex">Shader stage where the texture is used</param>
        /// <param name="handle">Offset in words of the texture handle on the texture buffer</param>
        /// <param name="cbufSlot">Slot of the texture buffer constant buffer</param>
        public void RecordTextureSamplerType(int stageIndex, int handle, int cbufSlot)
        {
            Box<TextureSpecializationState> state = GetOrCreateTextureSpecState(stageIndex, handle, cbufSlot);
            state.Value.QueriedFlags |= QueriedTextureStateFlags.SamplerType;
        }

        /// <summary>
        /// Indicates that the coordinate normalization state of a given texture was used during the shader translation process.
        /// </summary>
        /// <param name="stageIndex">Shader stage where the texture is used</param>
        /// <param name="handle">Offset in words of the texture handle on the texture buffer</param>
        /// <param name="cbufSlot">Slot of the texture buffer constant buffer</param>
        public void RecordTextureCoordNormalized(int stageIndex, int handle, int cbufSlot)
        {
            Box<TextureSpecializationState> state = GetOrCreateTextureSpecState(stageIndex, handle, cbufSlot);
            state.Value.QueriedFlags |= QueriedTextureStateFlags.CoordNormalized;
        }

        /// <summary>
        /// Checks if a given texture was registerd on this specialization state.
        /// </summary>
        /// <param name="stageIndex">Shader stage where the texture is used</param>
        /// <param name="handle">Offset in words of the texture handle on the texture buffer</param>
        /// <param name="cbufSlot">Slot of the texture buffer constant buffer</param>
        public bool TextureRegistered(int stageIndex, int handle, int cbufSlot)
        {
            return GetTextureSpecState(stageIndex, handle, cbufSlot) != null;
        }

        /// <summary>
        /// Gets the recorded format of a given texture.
        /// </summary>
        /// <param name="stageIndex">Shader stage where the texture is used</param>
        /// <param name="handle">Offset in words of the texture handle on the texture buffer</param>
        /// <param name="cbufSlot">Slot of the texture buffer constant buffer</param>
        public (uint, bool) GetFormat(int stageIndex, int handle, int cbufSlot)
        {
            TextureSpecializationState state = GetTextureSpecState(stageIndex, handle, cbufSlot).Value;
            return (state.Format, state.FormatSrgb);
        }

        /// <summary>
        /// Gets the recorded target of a given texture.
        /// </summary>
        /// <param name="stageIndex">Shader stage where the texture is used</param>
        /// <param name="handle">Offset in words of the texture handle on the texture buffer</param>
        /// <param name="cbufSlot">Slot of the texture buffer constant buffer</param>
        public Image.TextureTarget GetTextureTarget(int stageIndex, int handle, int cbufSlot)
        {
            return GetTextureSpecState(stageIndex, handle, cbufSlot).Value.TextureTarget;
        }

        /// <summary>
        /// Gets the recorded coordinate normalization state of a given texture.
        /// </summary>
        /// <param name="stageIndex">Shader stage where the texture is used</param>
        /// <param name="handle">Offset in words of the texture handle on the texture buffer</param>
        /// <param name="cbufSlot">Slot of the texture buffer constant buffer</param>
        public bool GetCoordNormalized(int stageIndex, int handle, int cbufSlot)
        {
            return GetTextureSpecState(stageIndex, handle, cbufSlot).Value.CoordNormalized;
        }

        /// <summary>
        /// Gets texture specialization state for a given texture, or create a new one if not present.
        /// </summary>
        /// <param name="stageIndex">Shader stage where the texture is used</param>
        /// <param name="handle">Offset in words of the texture handle on the texture buffer</param>
        /// <param name="cbufSlot">Slot of the texture buffer constant buffer</param>
        /// <returns>Texture specialization state</returns>
        private Box<TextureSpecializationState> GetOrCreateTextureSpecState(int stageIndex, int handle, int cbufSlot)
        {
            TextureKey key = new TextureKey(stageIndex, handle, cbufSlot);

            if (!_textureSpecialization.TryGetValue(key, out Box<TextureSpecializationState> state))
            {
                _textureSpecialization.Add(key, state = new Box<TextureSpecializationState>());
            }

            return state;
        }

        /// <summary>
        /// Gets texture specialization state for a given texture.
        /// </summary>
        /// <param name="stageIndex">Shader stage where the texture is used</param>
        /// <param name="handle">Offset in words of the texture handle on the texture buffer</param>
        /// <param name="cbufSlot">Slot of the texture buffer constant buffer</param>
        /// <returns>Texture specialization state</returns>
        private Box<TextureSpecializationState> GetTextureSpecState(int stageIndex, int handle, int cbufSlot)
        {
            TextureKey key = new TextureKey(stageIndex, handle, cbufSlot);

            if (_textureSpecialization.TryGetValue(key, out Box<TextureSpecializationState> state))
            {
                return state;
            }

            return null;
        }

        /// <summary>
        /// Checks if the recorded state matches the current GPU 3D engine state.
        /// </summary>
        /// <param name="channel">GPU channel</param>
        /// <param name="poolState">Texture pool state</param>
        /// <param name="graphicsState">Graphics state</param>
        /// <param name="checkTextures">Indicates whether texture descriptors should be checked</param>
        /// <returns>True if the state matches, false otherwise</returns>
        public bool MatchesGraphics(GpuChannel channel, GpuChannelPoolState poolState, GpuChannelGraphicsState graphicsState, bool checkTextures)
        {
            if (graphicsState.ViewportTransformDisable != GraphicsState.ViewportTransformDisable)
            {
                return false;
            }

            return Matches(channel, poolState, checkTextures, isCompute: false);
        }

        /// <summary>
        /// Checks if the recorded state matches the current GPU compute engine state.
        /// </summary>
        /// <param name="channel">GPU channel</param>
        /// <param name="poolState">Texture pool state</param>
        /// <param name="checkTextures">Indicates whether texture descriptors should be checked</param>
        /// <returns>True if the state matches, false otherwise</returns>
        public bool MatchesCompute(GpuChannel channel, GpuChannelPoolState poolState, bool checkTextures)
        {
            return Matches(channel, poolState, checkTextures, isCompute: true);
        }

        /// <summary>
        /// Fetch the constant buffers used for a texture to cache.
        /// </summary>
        /// <param name="channel">GPU channel</param>
        /// <param name="isCompute">Indicates whenever the check is requested by the 3D or compute engine</param>
        /// <param name="cachedTextureBufferIndex">The currently cached texture buffer index</param>
        /// <param name="cachedSamplerBufferIndex">The currently cached sampler buffer index</param>
        /// <param name="cachedTextureBuffer">The currently cached texture buffer data</param>
        /// <param name="cachedSamplerBuffer">The currently cached sampler buffer data</param>
        /// <param name="cachedStageIndex">The currently cached stage</param>
        /// <param name="textureBufferIndex">The new texture buffer index</param>
        /// <param name="samplerBufferIndex">The new sampler buffer index</param>
        /// <param name="stageIndex">Stage index of the constant buffer</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void UpdateCachedBuffer(
            GpuChannel channel,
            bool isCompute,
            ref int cachedTextureBufferIndex,
            ref int cachedSamplerBufferIndex,
            ref ReadOnlySpan<int> cachedTextureBuffer,
            ref ReadOnlySpan<int> cachedSamplerBuffer,
            ref int cachedStageIndex,
            int textureBufferIndex,
            int samplerBufferIndex,
            int stageIndex)
        {
            bool stageChange = stageIndex != cachedStageIndex;

            if (stageChange || textureBufferIndex != cachedTextureBufferIndex)
            {
                ref BufferBounds bounds = ref channel.BufferManager.GetUniformBufferBounds(isCompute, stageIndex, textureBufferIndex);

                cachedTextureBuffer = MemoryMarshal.Cast<byte, int>(channel.MemoryManager.Physical.GetSpan(bounds.Address, (int)bounds.Size));
                cachedTextureBufferIndex = textureBufferIndex;

                if (samplerBufferIndex == textureBufferIndex)
                {
                    cachedSamplerBuffer = cachedTextureBuffer;
                    cachedSamplerBufferIndex = samplerBufferIndex;
                }
            }

            if (stageChange || samplerBufferIndex != cachedSamplerBufferIndex)
            {
                ref BufferBounds bounds = ref channel.BufferManager.GetUniformBufferBounds(isCompute, stageIndex, samplerBufferIndex);

                cachedSamplerBuffer = MemoryMarshal.Cast<byte, int>(channel.MemoryManager.Physical.GetSpan(bounds.Address, (int)bounds.Size));
                cachedSamplerBufferIndex = samplerBufferIndex;
            }

            cachedStageIndex = stageIndex;
        }

        /// <summary>
        /// Checks if the recorded state matches the current GPU state.
        /// </summary>
        /// <param name="channel">GPU channel</param>
        /// <param name="poolState">Texture pool state</param>
        /// <param name="checkTextures">Indicates whether texture descriptors should be checked</param>
        /// <param name="isCompute">Indicates whenever the check is requested by the 3D or compute engine</param>
        /// <returns>True if the state matches, false otherwise</returns>
        private bool Matches(GpuChannel channel, GpuChannelPoolState poolState, bool checkTextures, bool isCompute)
        {
            int constantBufferUsePerStageMask = _constantBufferUsePerStage;

            while (constantBufferUsePerStageMask != 0)
            {
                int index = BitOperations.TrailingZeroCount(constantBufferUsePerStageMask);

                uint useMask = isCompute
                    ? channel.BufferManager.GetComputeUniformBufferUseMask()
                    : channel.BufferManager.GetGraphicsUniformBufferUseMask(index);

                if (ConstantBufferUse[index] != useMask)
                {
                    return false;
                }

                constantBufferUsePerStageMask &= ~(1 << index);
            }

            if (checkTextures)
            {
                TexturePool pool = channel.TextureManager.GetTexturePool(poolState.TexturePoolGpuVa, poolState.TexturePoolMaximumId);

                int cachedTextureBufferIndex = -1;
                int cachedSamplerBufferIndex = -1;
                int cachedStageIndex = -1;
                ReadOnlySpan<int> cachedTextureBuffer = Span<int>.Empty;
                ReadOnlySpan<int> cachedSamplerBuffer = Span<int>.Empty;

                foreach (var kv in _allTextures)
                {
                    TextureKey textureKey = kv.Key;

                    (int textureBufferIndex, int samplerBufferIndex) = TextureHandle.UnpackSlots(textureKey.CbufSlot, poolState.TextureBufferIndex);

                    UpdateCachedBuffer(channel,
                        isCompute,
                        ref cachedTextureBufferIndex,
                        ref cachedSamplerBufferIndex,
                        ref cachedTextureBuffer,
                        ref cachedSamplerBuffer,
                        ref cachedStageIndex,
                        textureBufferIndex,
                        samplerBufferIndex,
                        textureKey.StageIndex);

                    int packedId = TextureHandle.ReadPackedId(textureKey.Handle, cachedTextureBuffer, cachedSamplerBuffer);
                    int textureId = TextureHandle.UnpackTextureId(packedId);

                    if (pool.IsValidId(textureId))
                    {
                        ref readonly Image.TextureDescriptor descriptor = ref pool.GetDescriptorRef(textureId);

                        if (!MatchesTexture(kv.Value, descriptor))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Checks if the recorded texture state matches the given texture descriptor.
        /// </summary>
        /// <param name="specializationState">Texture specialization state</param>
        /// <param name="descriptor">Texture descriptor</param>
        /// <returns>True if the state matches, false otherwise</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool MatchesTexture(Box<TextureSpecializationState> specializationState, in Image.TextureDescriptor descriptor)
        {
            if (specializationState != null)
            {
                if (specializationState.Value.QueriedFlags.HasFlag(QueriedTextureStateFlags.CoordNormalized) &&
                    specializationState.Value.CoordNormalized != descriptor.UnpackTextureCoordNormalized())
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checks if the recorded texture state for a given texture binding matches a texture descriptor.
        /// </summary>
        /// <param name="stage">The shader stage</param>
        /// <param name="index">The texture index</param>
        /// <param name="descriptor">Texture descriptor</param>
        /// <returns>True if the state matches, false otherwise</returns>
        public bool MatchesTexture(ShaderStage stage, int index, in Image.TextureDescriptor descriptor)
        {
            Box<TextureSpecializationState> specializationState = _textureByBinding[(int)stage][index];

            return MatchesTexture(specializationState, descriptor);
        }

        /// <summary>
        /// Checks if the recorded texture state for a given image binding matches a texture descriptor.
        /// </summary>
        /// <param name="stage">The shader stage</param>
        /// <param name="index">The texture index</param>
        /// <param name="descriptor">Texture descriptor</param>
        /// <returns>True if the state matches, false otherwise</returns>
        public bool MatchesImage(ShaderStage stage, int index, in Image.TextureDescriptor descriptor)
        {
            Box<TextureSpecializationState> specializationState = _imageByBinding[(int)stage][index];

            return MatchesTexture(specializationState, descriptor);
        }

        /// <summary>
        /// Reads shader specialization state that has been serialized.
        /// </summary>
        /// <param name="dataReader">Data reader</param>
        /// <returns>Shader specialization state</returns>
        public static ShaderSpecializationState Read(ref BinarySerializer dataReader)
        {
            ShaderSpecializationState specState = new ShaderSpecializationState();

            dataReader.Read(ref specState._queriedState);
            dataReader.Read(ref specState._compute);

            if (specState._compute)
            {
                dataReader.ReadWithMagicAndSize(ref specState.ComputeState, ComsMagic);
            }
            else
            {
                dataReader.ReadWithMagicAndSize(ref specState.GraphicsState, GfxsMagic);
            }

            dataReader.Read(ref specState._constantBufferUsePerStage);

            int constantBufferUsePerStageMask = specState._constantBufferUsePerStage;

            while (constantBufferUsePerStageMask != 0)
            {
                int index = BitOperations.TrailingZeroCount(constantBufferUsePerStageMask);
                dataReader.Read(ref specState.ConstantBufferUse[index]);
                constantBufferUsePerStageMask &= ~(1 << index);
            }

            if (specState._queriedState.HasFlag(QueriedStateFlags.TransformFeedback))
            {
                ushort tfCount = 0;
                dataReader.Read(ref tfCount);
                specState.TransformFeedbackDescriptors = new TransformFeedbackDescriptor[tfCount];

                for (int index = 0; index < tfCount; index++)
                {
                    dataReader.ReadWithMagicAndSize(ref specState.TransformFeedbackDescriptors[index], TfbdMagic);
                }
            }

            ushort count = 0;
            dataReader.Read(ref count);

            for (int index = 0; index < count; index++)
            {
                TextureKey textureKey = default;
                Box<TextureSpecializationState> textureState = new Box<TextureSpecializationState>();

                dataReader.ReadWithMagicAndSize(ref textureKey, TexkMagic);
                dataReader.ReadWithMagicAndSize(ref textureState.Value, TexsMagic);

                specState._textureSpecialization[textureKey] = textureState;
            }

            return specState;
        }

        /// <summary>
        /// Serializes the shader specialization state.
        /// </summary>
        /// <param name="dataWriter">Data writer</param>
        public void Write(ref BinarySerializer dataWriter)
        {
            dataWriter.Write(ref _queriedState);
            dataWriter.Write(ref _compute);

            if (_compute)
            {
                dataWriter.WriteWithMagicAndSize(ref ComputeState, ComsMagic);
            }
            else
            {
                dataWriter.WriteWithMagicAndSize(ref GraphicsState, GfxsMagic);
            }

            dataWriter.Write(ref _constantBufferUsePerStage);

            int constantBufferUsePerStageMask = _constantBufferUsePerStage;

            while (constantBufferUsePerStageMask != 0)
            {
                int index = BitOperations.TrailingZeroCount(constantBufferUsePerStageMask);
                dataWriter.Write(ref ConstantBufferUse[index]);
                constantBufferUsePerStageMask &= ~(1 << index);
            }

            if (_queriedState.HasFlag(QueriedStateFlags.TransformFeedback))
            {
                ushort tfCount = (ushort)TransformFeedbackDescriptors.Length;
                dataWriter.Write(ref tfCount);

                for (int index = 0; index < TransformFeedbackDescriptors.Length; index++)
                {
                    dataWriter.WriteWithMagicAndSize(ref TransformFeedbackDescriptors[index], TfbdMagic);
                }
            }

            ushort count = (ushort)_textureSpecialization.Count;
            dataWriter.Write(ref count);

            foreach (var kv in _textureSpecialization)
            {
                var textureKey = kv.Key;
                var textureState = kv.Value;

                dataWriter.WriteWithMagicAndSize(ref textureKey, TexkMagic);
                dataWriter.WriteWithMagicAndSize(ref textureState.Value, TexsMagic);
            }
        }
    }
}