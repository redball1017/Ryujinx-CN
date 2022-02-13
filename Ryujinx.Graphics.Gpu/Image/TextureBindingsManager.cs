using Ryujinx.Common.Logging;
using Ryujinx.Graphics.GAL;
using Ryujinx.Graphics.Gpu.Engine.Types;
using Ryujinx.Graphics.Shader;
using System;

namespace Ryujinx.Graphics.Gpu.Image
{
    /// <summary>
    /// Texture bindings manager.
    /// </summary>
    class TextureBindingsManager : IDisposable
    {
        private const int InitialTextureStateSize = 32;
        private const int InitialImageStateSize = 8;

        private readonly GpuContext _context;

        private readonly bool _isCompute;

        private SamplerPool _samplerPool;

        private SamplerIndex _samplerIndex;

        private ulong _texturePoolAddress;
        private int   _texturePoolMaximumId;

        private readonly GpuChannel _channel;
        private readonly TexturePoolCache _texturePoolCache;

        private readonly TextureBindingInfo[][] _textureBindings;
        private readonly TextureBindingInfo[][] _imageBindings;

        private struct TextureStatePerStage
        {
            public ITexture Texture;
            public ISampler Sampler;
        }

        private readonly TextureStatePerStage[][] _textureState;
        private readonly TextureStatePerStage[][] _imageState;

        private int[] _textureBindingsCount;
        private int[] _imageBindingsCount;

        private int _textureBufferIndex;

        private bool _rebind;

        private readonly float[] _scales;
        private bool _scaleChanged;
        private int _lastFragmentTotal;

        /// <summary>
        /// Constructs a new instance of the texture bindings manager.
        /// </summary>
        /// <param name="context">The GPU context that the texture bindings manager belongs to</param>
        /// <param name="channel">The GPU channel that the texture bindings manager belongs to</param>
        /// <param name="poolCache">Texture pools cache used to get texture pools from</param>
        /// <param name="scales">Array where the scales for the currently bound textures are stored</param>
        /// <param name="isCompute">True if the bindings manager is used for the compute engine</param>
        public TextureBindingsManager(GpuContext context, GpuChannel channel, TexturePoolCache poolCache, float[] scales, bool isCompute)
        {
            _context          = context;
            _channel          = channel;
            _texturePoolCache = poolCache;
            _scales           = scales;
            _isCompute        = isCompute;

            int stages = isCompute ? 1 : Constants.ShaderStages;

            _textureBindings = new TextureBindingInfo[stages][];
            _imageBindings   = new TextureBindingInfo[stages][];

            _textureState = new TextureStatePerStage[stages][];
            _imageState   = new TextureStatePerStage[stages][];

            _textureBindingsCount = new int[stages];
            _imageBindingsCount = new int[stages];

            for (int stage = 0; stage < stages; stage++)
            {
                _textureBindings[stage] = new TextureBindingInfo[InitialTextureStateSize];
                _imageBindings[stage] = new TextureBindingInfo[InitialImageStateSize];

                _textureState[stage] = new TextureStatePerStage[InitialTextureStateSize];
                _imageState[stage] = new TextureStatePerStage[InitialImageStateSize];
            }
        }

        /// <summary>
        /// Rents the texture bindings array for a given stage, so that they can be modified.
        /// </summary>
        /// <param name="stage">Shader stage number, or 0 for compute shaders</param>
        /// <param name="count">The number of bindings needed</param>
        /// <returns>The texture bindings array</returns>
        public TextureBindingInfo[] RentTextureBindings(int stage, int count)
        {
            if (count > _textureBindings[stage].Length)
            {
                Array.Resize(ref _textureBindings[stage], count);
                Array.Resize(ref _textureState[stage], count);
            }

            int toClear = Math.Max(_textureBindingsCount[stage], count);
            TextureStatePerStage[] state = _textureState[stage];

            for (int i = 0; i < toClear; i++)
            {
                state[i] = new TextureStatePerStage();
            }

            _textureBindingsCount[stage] = count;

            return _textureBindings[stage];
        }

        /// <summary>
        /// Rents the image bindings array for a given stage, so that they can be modified.
        /// </summary>
        /// <param name="stage">Shader stage number, or 0 for compute shaders</param>
        /// <param name="count">The number of bindings needed</param>
        /// <returns>The image bindings array</returns>
        public TextureBindingInfo[] RentImageBindings(int stage, int count)
        {
            if (count > _imageBindings[stage].Length)
            {
                Array.Resize(ref _imageBindings[stage], count);
                Array.Resize(ref _imageState[stage], count);
            }

            int toClear = Math.Max(_imageBindingsCount[stage], count);
            TextureStatePerStage[] state = _imageState[stage];

            for (int i = 0; i < toClear; i++)
            {
                state[i] = new TextureStatePerStage();
            }

            _imageBindingsCount[stage] = count;

            return _imageBindings[stage];
        }

        /// <summary>
        /// Sets the textures constant buffer index.
        /// The constant buffer specified holds the texture handles.
        /// </summary>
        /// <param name="index">Constant buffer index</param>
        public void SetTextureBufferIndex(int index)
        {
            _textureBufferIndex = index;
        }

        /// <summary>
        /// Sets the current texture sampler pool to be used.
        /// </summary>
        /// <param name="gpuVa">Start GPU virtual address of the pool</param>
        /// <param name="maximumId">Maximum ID of the pool (total count minus one)</param>
        /// <param name="samplerIndex">Type of the sampler pool indexing used for bound samplers</param>
        public void SetSamplerPool(ulong gpuVa, int maximumId, SamplerIndex samplerIndex)
        {
            if (gpuVa != 0)
            {
                ulong address = _channel.MemoryManager.Translate(gpuVa);

                if (_samplerPool != null && _samplerPool.Address == address && _samplerPool.MaximumId >= maximumId)
                {
                    return;
                }

                _samplerPool?.Dispose();
                _samplerPool = new SamplerPool(_context, _channel.MemoryManager.Physical, address, maximumId);
            }
            else
            {
                _samplerPool?.Dispose();
                _samplerPool = null;
            }

            _samplerIndex = samplerIndex;
        }

        /// <summary>
        /// Sets the current texture pool to be used.
        /// </summary>
        /// <param name="gpuVa">Start GPU virtual address of the pool</param>
        /// <param name="maximumId">Maximum ID of the pool (total count minus one)</param>
        public void SetTexturePool(ulong gpuVa, int maximumId)
        {
            if (gpuVa != 0)
            {
                ulong address = _channel.MemoryManager.Translate(gpuVa);

                _texturePoolAddress = address;
                _texturePoolMaximumId = maximumId;
            }
            else
            {
                _texturePoolAddress = 0;
                _texturePoolMaximumId = 0;
            }
        }

        /// <summary>
        /// Gets a texture and a sampler from their respective pools from a texture ID and a sampler ID.
        /// </summary>
        /// <param name="textureId">ID of the texture</param>
        /// <param name="samplerId">ID of the sampler</param>
        public (Texture, Sampler) GetTextureAndSampler(int textureId, int samplerId)
        {
            ulong texturePoolAddress = _texturePoolAddress;

            TexturePool texturePool = texturePoolAddress != 0
                ? _texturePoolCache.FindOrCreate(_channel, texturePoolAddress, _texturePoolMaximumId)
                : null;

            return (texturePool.Get(textureId), _samplerPool.Get(samplerId));
        }

        /// <summary>
        /// Updates the texture scale for a given texture or image.
        /// </summary>
        /// <param name="texture">Start GPU virtual address of the pool</param>
        /// <param name="binding">The related texture binding</param>
        /// <param name="index">The texture/image binding index</param>
        /// <param name="stage">The active shader stage</param>
        /// <returns>True if the given texture has become blacklisted, indicating that its host texture may have changed.</returns>
        private bool UpdateScale(Texture texture, TextureBindingInfo binding, int index, ShaderStage stage)
        {
            float result = 1f;
            bool changed = false;

            if ((binding.Flags & TextureUsageFlags.NeedsScaleValue) != 0 && texture != null)
            {
                if ((binding.Flags & TextureUsageFlags.ResScaleUnsupported) != 0)
                {
                    changed = texture.ScaleMode != TextureScaleMode.Blacklisted;
                    texture.BlacklistScale();
                }
                else
                {
                    switch (stage)
                    {
                        case ShaderStage.Fragment:
                            float scale = texture.ScaleFactor;

                            if (scale != 1)
                            {
                                Texture activeTarget = _channel.TextureManager.GetAnyRenderTarget();

                                if (activeTarget != null && (activeTarget.Info.Width / (float)texture.Info.Width) == (activeTarget.Info.Height / (float)texture.Info.Height))
                                {
                                    // If the texture's size is a multiple of the sampler size, enable interpolation using gl_FragCoord. (helps "invent" new integer values between scaled pixels)
                                    result = -scale;
                                    break;
                                }
                            }

                            result = scale;
                            break;

                        case ShaderStage.Vertex:
                            int fragmentIndex = (int)ShaderStage.Fragment - 1;
                            index += _textureBindingsCount[fragmentIndex] + _imageBindingsCount[fragmentIndex];

                            result = texture.ScaleFactor;
                            break;

                        case ShaderStage.Compute:
                            result = texture.ScaleFactor;
                            break;
                    }
                }
            }

            if (result != _scales[index])
            {
                _scaleChanged = true;

                _scales[index] = result;
            }

            return changed;
        }

        /// <summary>
        /// Uploads texture and image scales to the backend when they are used.
        /// </summary>
        private void CommitRenderScale()
        {
            // Stage 0 total: Compute or Vertex.
            int total = _textureBindingsCount[0] + _imageBindingsCount[0];

            int fragmentIndex = (int)ShaderStage.Fragment - 1;
            int fragmentTotal = _isCompute ? 0 : (_textureBindingsCount[fragmentIndex] + _imageBindingsCount[fragmentIndex]);

            if (total != 0 && fragmentTotal != _lastFragmentTotal)
            {
                // Must update scales in the support buffer if:
                // - Vertex stage has bindings.
                // - Fragment stage binding count has been updated since last render scale update.

                _scaleChanged = true;
            }

            if (_scaleChanged)
            {
                if (!_isCompute)
                {
                    total += fragmentTotal; // Add the fragment bindings to the total.
                }

                _lastFragmentTotal = fragmentTotal;

                _context.Renderer.Pipeline.UpdateRenderScale(_scales, total, fragmentTotal);

                _scaleChanged = false;
            }
        }

        /// <summary>
        /// Ensures that the bindings are visible to the host GPU.
        /// Note: this actually performs the binding using the host graphics API.
        /// </summary>
        public void CommitBindings()
        {
            ulong texturePoolAddress = _texturePoolAddress;

            TexturePool texturePool = texturePoolAddress != 0
                ? _texturePoolCache.FindOrCreate(_channel, texturePoolAddress, _texturePoolMaximumId)
                : null;

            if (_isCompute)
            {
                CommitTextureBindings(texturePool, ShaderStage.Compute, 0);
                CommitImageBindings  (texturePool, ShaderStage.Compute, 0);
            }
            else
            {
                for (ShaderStage stage = ShaderStage.Vertex; stage <= ShaderStage.Fragment; stage++)
                {
                    int stageIndex = (int)stage - 1;

                    CommitTextureBindings(texturePool, stage, stageIndex);
                    CommitImageBindings  (texturePool, stage, stageIndex);
                }
            }

            CommitRenderScale();

            _rebind = false;
        }

        /// <summary>
        /// Counts the total number of texture bindings used by all shader stages.
        /// </summary>
        /// <returns>The total amount of textures used</returns>
        private int GetTextureBindingsCount()
        {
            int count = 0;

            for (int i = 0; i < _textureBindings.Length; i++)
            {
                if (_textureBindings[i] != null)
                {
                    count += _textureBindings[i].Length;
                }
            }

            return count;
        }

        /// <summary>
        /// Ensures that the texture bindings are visible to the host GPU.
        /// Note: this actually performs the binding using the host graphics API.
        /// </summary>
        /// <param name="pool">The current texture pool</param>
        /// <param name="stage">The shader stage using the textures to be bound</param>
        /// <param name="stageIndex">The stage number of the specified shader stage</param>
        private void CommitTextureBindings(TexturePool pool, ShaderStage stage, int stageIndex)
        {
            int textureCount = _textureBindingsCount[stageIndex];
            if (textureCount == 0)
            {
                return;
            }

            var samplerPool = _samplerPool;

            if (pool == null)
            {
                Logger.Error?.Print(LogClass.Gpu, $"Shader stage \"{stage}\" uses textures, but texture pool was not set.");
                return;
            }

            for (int index = 0; index < textureCount; index++)
            {
                TextureBindingInfo bindingInfo = _textureBindings[stageIndex][index];

                (int textureBufferIndex, int samplerBufferIndex) = TextureHandle.UnpackSlots(bindingInfo.CbufSlot, _textureBufferIndex);

                int packedId = ReadPackedId(stageIndex, bindingInfo.Handle, textureBufferIndex, samplerBufferIndex);
                int textureId = UnpackTextureId(packedId);
                int samplerId;

                if (_samplerIndex == SamplerIndex.ViaHeaderIndex)
                {
                    samplerId = textureId;
                }
                else
                {
                    samplerId = UnpackSamplerId(packedId);
                }

                Texture texture = pool.Get(textureId);
                Sampler sampler = _samplerPool?.Get(samplerId);

                ITexture hostTexture = texture?.GetTargetTexture(bindingInfo.Target);
                ISampler hostSampler = sampler?.GetHostSampler(texture);

                if (hostTexture != null && texture.Target == Target.TextureBuffer)
                {
                    // Ensure that the buffer texture is using the correct buffer as storage.
                    // Buffers are frequently re-created to accomodate larger data, so we need to re-bind
                    // to ensure we're not using a old buffer that was already deleted.
                    _channel.BufferManager.SetBufferTextureStorage(hostTexture, texture.Range.GetSubRange(0).Address, texture.Size, bindingInfo, bindingInfo.Format, false);
                }
                else if (_textureState[stageIndex][index].Texture != hostTexture ||
                         _textureState[stageIndex][index].Sampler != hostSampler ||
                         _rebind)
                {
                    if (UpdateScale(texture, bindingInfo, index, stage))
                    {
                        hostTexture = texture?.GetTargetTexture(bindingInfo.Target);
                    }

                    _textureState[stageIndex][index].Texture = hostTexture;
                    _textureState[stageIndex][index].Sampler = hostSampler;

                    _context.Renderer.Pipeline.SetTextureAndSampler(bindingInfo.Binding, hostTexture, hostSampler);
                }
            }
        }

        /// <summary>
        /// Ensures that the image bindings are visible to the host GPU.
        /// Note: this actually performs the binding using the host graphics API.
        /// </summary>
        /// <param name="pool">The current texture pool</param>
        /// <param name="stage">The shader stage using the textures to be bound</param>
        /// <param name="stageIndex">The stage number of the specified shader stage</param>
        private void CommitImageBindings(TexturePool pool, ShaderStage stage, int stageIndex)
        {
            int imageCount = _imageBindingsCount[stageIndex];
            if (imageCount == 0)
            {
                return;
            }

            if (pool == null)
            {
                Logger.Error?.Print(LogClass.Gpu, $"Shader stage \"{stage}\" uses images, but texture pool was not set.");
                return;
            }

            // Scales for images appear after the texture ones.
            int baseScaleIndex = _textureBindingsCount[stageIndex];

            for (int index = 0; index < imageCount; index++)
            {
                TextureBindingInfo bindingInfo = _imageBindings[stageIndex][index];

                (int textureBufferIndex, int samplerBufferIndex) = TextureHandle.UnpackSlots(bindingInfo.CbufSlot, _textureBufferIndex);

                int packedId = ReadPackedId(stageIndex, bindingInfo.Handle, textureBufferIndex, samplerBufferIndex);
                int textureId = UnpackTextureId(packedId);

                Texture texture = pool.Get(textureId);

                ITexture hostTexture = texture?.GetTargetTexture(bindingInfo.Target);

                bool isStore = bindingInfo.Flags.HasFlag(TextureUsageFlags.ImageStore);

                if (hostTexture != null && texture.Target == Target.TextureBuffer)
                {
                    // Ensure that the buffer texture is using the correct buffer as storage.
                    // Buffers are frequently re-created to accomodate larger data, so we need to re-bind
                    // to ensure we're not using a old buffer that was already deleted.

                    Format format = bindingInfo.Format;

                    if (format == 0 && texture != null)
                    {
                        format = texture.Format;
                    }

                    _channel.BufferManager.SetBufferTextureStorage(hostTexture, texture.Range.GetSubRange(0).Address, texture.Size, bindingInfo, format, true);
                }
                else
                {
                    if (isStore)
                    {
                        texture?.SignalModified();
                    }

                    if (_imageState[stageIndex][index].Texture != hostTexture || _rebind)
                    {
                        if (UpdateScale(texture, bindingInfo, baseScaleIndex + index, stage))
                        {
                            hostTexture = texture?.GetTargetTexture(bindingInfo.Target);
                        }

                        _imageState[stageIndex][index].Texture = hostTexture;

                        Format format = bindingInfo.Format;

                        if (format == 0 && texture != null)
                        {
                            format = texture.Format;
                        }

                        _context.Renderer.Pipeline.SetImage(bindingInfo.Binding, hostTexture, format);
                    }
                }
            }
        }

        /// <summary>
        /// Gets the texture descriptor for a given texture handle.
        /// </summary>
        /// <param name="poolGpuVa">GPU virtual address of the texture pool</param>
        /// <param name="bufferIndex">Index of the constant buffer with texture handles</param>
        /// <param name="maximumId">Maximum ID of the texture pool</param>
        /// <param name="stageIndex">The stage number where the texture is bound</param>
        /// <param name="handle">The texture handle</param>
        /// <param name="cbufSlot">The texture handle's constant buffer slot</param>
        /// <returns>The texture descriptor for the specified texture</returns>
        public TextureDescriptor GetTextureDescriptor(
            ulong poolGpuVa,
            int bufferIndex,
            int maximumId,
            int stageIndex,
            int handle,
            int cbufSlot)
        {
            (int textureBufferIndex, int samplerBufferIndex) = TextureHandle.UnpackSlots(cbufSlot, bufferIndex);

            int packedId = ReadPackedId(stageIndex, handle, textureBufferIndex, samplerBufferIndex);
            int textureId = UnpackTextureId(packedId);

            ulong poolAddress = _channel.MemoryManager.Translate(poolGpuVa);

            TexturePool texturePool = _texturePoolCache.FindOrCreate(_channel, poolAddress, maximumId);

            return texturePool.GetDescriptor(textureId);
        }

        /// <summary>
        /// Reads a packed texture and sampler ID (basically, the real texture handle)
        /// from the texture constant buffer.
        /// </summary>
        /// <param name="stageIndex">The number of the shader stage where the texture is bound</param>
        /// <param name="wordOffset">A word offset of the handle on the buffer (the "fake" shader handle)</param>
        /// <param name="textureBufferIndex">Index of the constant buffer holding the texture handles</param>
        /// <param name="samplerBufferIndex">Index of the constant buffer holding the sampler handles</param>
        /// <returns>The packed texture and sampler ID (the real texture handle)</returns>
        private int ReadPackedId(int stageIndex, int wordOffset, int textureBufferIndex, int samplerBufferIndex)
        {
            (int textureWordOffset, int samplerWordOffset, TextureHandleType handleType) = TextureHandle.UnpackOffsets(wordOffset);

            ulong textureBufferAddress = _isCompute
                ? _channel.BufferManager.GetComputeUniformBufferAddress(textureBufferIndex)
                : _channel.BufferManager.GetGraphicsUniformBufferAddress(stageIndex, textureBufferIndex);

            int handle = _channel.MemoryManager.Physical.Read<int>(textureBufferAddress + (uint)textureWordOffset * 4);

            // The "wordOffset" (which is really the immediate value used on texture instructions on the shader)
            // is a 13-bit value. However, in order to also support separate samplers and textures (which uses
            // bindless textures on the shader), we extend it with another value on the higher 16 bits with
            // another offset for the sampler.
            // The shader translator has code to detect separate texture and sampler uses with a bindless texture,
            // turn that into a regular texture access and produce those special handles with values on the higher 16 bits.
            if (handleType != TextureHandleType.CombinedSampler)
            {
                ulong samplerBufferAddress = _isCompute
                    ? _channel.BufferManager.GetComputeUniformBufferAddress(samplerBufferIndex)
                    : _channel.BufferManager.GetGraphicsUniformBufferAddress(stageIndex, samplerBufferIndex);

                int samplerHandle = _channel.MemoryManager.Physical.Read<int>(samplerBufferAddress + (uint)samplerWordOffset * 4);

                if (handleType == TextureHandleType.SeparateSamplerId)
                {
                    samplerHandle <<= 20;
                }

                handle |= samplerHandle;
            }

            return handle;
        }

        /// <summary>
        /// Unpacks the texture ID from the real texture handle.
        /// </summary>
        /// <param name="packedId">The real texture handle</param>
        /// <returns>The texture ID</returns>
        private static int UnpackTextureId(int packedId)
        {
            return (packedId >> 0) & 0xfffff;
        }

        /// <summary>
        /// Unpacks the sampler ID from the real texture handle.
        /// </summary>
        /// <param name="packedId">The real texture handle</param>
        /// <returns>The sampler ID</returns>
        private static int UnpackSamplerId(int packedId)
        {
            return (packedId >> 20) & 0xfff;
        }

        /// <summary>
        /// Force all bound textures and images to be rebound the next time CommitBindings is called.
        /// </summary>
        public void Rebind()
        {
            _rebind = true;
        }

        /// <summary>
        /// Disposes all textures and samplers in the cache.
        /// </summary>
        public void Dispose()
        {
            _samplerPool?.Dispose();
        }
    }
}