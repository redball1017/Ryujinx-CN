﻿using Ryujinx.Graphics.GAL;
using Ryujinx.Graphics.Shader;
using Silk.NET.Vulkan;
using System;

namespace Ryujinx.Graphics.Vulkan
{
    class PipelineBase : IDisposable
    {
        public const int DescriptorSetLayouts = 6;

        public const int UniformSetIndex = 0;
        public const int StorageSetIndex = 1;
        public const int TextureSetIndex = 2;
        public const int ImageSetIndex = 3;
        public const int BufferTextureSetIndex = 4;
        public const int BufferImageSetIndex = 5;

        protected readonly VulkanGraphicsDevice Gd;
        protected readonly Device Device;

        private PipelineDynamicState _dynamicState;
        private PipelineState _newState;
        private bool _stateDirty;
        private GAL.PrimitiveTopology _topology;

        private ulong _currentPipelineHandle;

        protected Auto<DisposablePipeline> Pipeline;

        protected PipelineBindPoint Pbp;

        private PipelineCache _pipelineCache;

        protected CommandBufferScoped Cbs;
        protected CommandBufferScoped? PreloadCbs;
        protected CommandBuffer CommandBuffer;

        public CommandBufferScoped CurrentCommandBuffer => Cbs;

        private ShaderCollection _program;

        private Vector4<float>[] _renderScale = new Vector4<float>[65];
        private int _fragmentScaleCount;

        protected FramebufferParams FramebufferParams;
        private Auto<DisposableFramebuffer> _framebuffer;
        private Auto<DisposableRenderPass> _renderPass;
        private bool _renderPassActive;

        private readonly DescriptorSetUpdater _descriptorSetUpdater;

        private BufferState _indexBuffer;
        private readonly BufferState[] _transformFeedbackBuffers;
        private readonly BufferState[] _vertexBuffers;

        public SupportBufferUpdater SupportBufferUpdater;

        private bool _needsIndexBufferRebind;
        private bool _needsTransformFeedbackBuffersRebind;
        private bool _needsVertexBuffersRebind;

        private bool _tfEnabled;
        private bool _tfActive;

        public ulong DrawCount { get; private set; }

        public unsafe PipelineBase(VulkanGraphicsDevice gd, Device device)
        {
            Gd = gd;
            Device = device;

            var pipelineCacheCreateInfo = new PipelineCacheCreateInfo()
            {
                SType = StructureType.PipelineCacheCreateInfo
            };

            gd.Api.CreatePipelineCache(device, pipelineCacheCreateInfo, null, out _pipelineCache).ThrowOnError();

            _descriptorSetUpdater = new DescriptorSetUpdater(gd, this);

            _transformFeedbackBuffers = new BufferState[Constants.MaxTransformFeedbackBuffers];
            _vertexBuffers = new BufferState[Constants.MaxVertexBuffers + 1];

            const int EmptyVbSize = 16;

            using var emptyVb = gd.BufferManager.Create(gd, EmptyVbSize);
            emptyVb.SetData(0, new byte[EmptyVbSize]);
            _vertexBuffers[0] = new BufferState(emptyVb.GetBuffer(), 0, EmptyVbSize, 0UL);
            _needsVertexBuffersRebind = true;

            var defaultScale = new Vector4<float> { X = 1f, Y = 0f, Z = 0f, W = 0f };
            new Span<Vector4<float>>(_renderScale).Fill(defaultScale);

            SupportBufferUpdater = new SupportBufferUpdater(gd);
            SupportBufferUpdater.UpdateRenderScale(_renderScale, 0, SupportBuffer.RenderScaleMaxCount);

            _newState.Initialize();
            _newState.LineWidth = 1f;
        }

        protected virtual DescriptorSetLayout[] CreateDescriptorSetLayouts(VulkanGraphicsDevice gd, Device device, out PipelineLayout layout)
        {
            throw new NotSupportedException();
        }

        public unsafe void Barrier()
        {
            MemoryBarrier memoryBarrier = new MemoryBarrier()
            {
                SType = StructureType.MemoryBarrier,
                SrcAccessMask = AccessFlags.AccessMemoryReadBit | AccessFlags.AccessMemoryWriteBit,
                DstAccessMask = AccessFlags.AccessMemoryReadBit | AccessFlags.AccessMemoryWriteBit
            };

            Gd.Api.CmdPipelineBarrier(
                CommandBuffer,
                PipelineStageFlags.PipelineStageFragmentShaderBit,
                PipelineStageFlags.PipelineStageFragmentShaderBit,
                0,
                1,
                memoryBarrier,
                0,
                null,
                0,
                null);
        }

        public void BeginTransformFeedback(GAL.PrimitiveTopology topology)
        {
            _tfEnabled = true;
        }

        public void ClearBuffer(BufferHandle destination, int offset, int size, uint value)
        {
            EndRenderPass();

            var dst = Gd.BufferManager.GetBuffer(CommandBuffer, destination, true).Get(Cbs, offset, size).Value;

            Gd.Api.CmdFillBuffer(CommandBuffer, dst, (ulong)offset, (ulong)size, value);
        }

        public unsafe void ClearRenderTargetColor(int index, uint componentMask, ColorF color)
        {
            // TODO: Use componentMask

            if (_framebuffer == null)
            {
                return;
            }

            if (_renderPass == null)
            {
                CreateRenderPass();
            }

            BeginRenderPass();

            var clearValue = new ClearValue(new ClearColorValue(color.Red, color.Green, color.Blue, color.Alpha));
            var attachment = new ClearAttachment(ImageAspectFlags.ImageAspectColorBit, (uint)index, clearValue);
            var clearRect = FramebufferParams?.GetClearRect() ?? default;

            Gd.Api.CmdClearAttachments(CommandBuffer, 1, &attachment, 1, &clearRect);
        }

        public unsafe void ClearRenderTargetDepthStencil(float depthValue, bool depthMask, int stencilValue, int stencilMask)
        {
            // TODO: Use stencilMask (fully)

            if (_framebuffer == null || !FramebufferParams.HasDepthStencil)
            {
                return;
            }

            if (_renderPass == null)
            {
                CreateRenderPass();
            }

            BeginRenderPass();

            var clearValue = new ClearValue(null, new ClearDepthStencilValue(depthValue, (uint)stencilValue));
            var flags = depthMask ? ImageAspectFlags.ImageAspectDepthBit : 0;

            if (stencilMask != 0)
            {
                flags |= ImageAspectFlags.ImageAspectStencilBit;
            }

            var attachment = new ClearAttachment(flags, 0, clearValue);
            var clearRect = FramebufferParams?.GetClearRect() ?? default;

            Gd.Api.CmdClearAttachments(CommandBuffer, 1, &attachment, 1, &clearRect);
        }

        public void CommandBufferBarrier()
        {
            // TODO: More specific barrier?
            Barrier();
        }

        public void CopyBuffer(BufferHandle source, BufferHandle destination, int srcOffset, int dstOffset, int size)
        {
            EndRenderPass();

            var src = Gd.BufferManager.GetBuffer(CommandBuffer, source, false);
            var dst = Gd.BufferManager.GetBuffer(CommandBuffer, destination, true);

            BufferHolder.Copy(Gd, Cbs, src, dst, srcOffset, dstOffset, size);
        }

        public void DispatchCompute(int groupsX, int groupsY, int groupsZ)
        {
            if (!_program.IsLinked)
            {
                return;
            }

            EndRenderPass();
            RecreatePipelineIfNeeded(PipelineBindPoint.Compute);

            Gd.Api.CmdDispatch(CommandBuffer, (uint)groupsX, (uint)groupsY, (uint)groupsZ);
        }

        public void Draw(int vertexCount, int instanceCount, int firstVertex, int firstInstance)
        {
            // System.Console.WriteLine("draw");

            if (!_program.IsLinked)
            {
                return;
            }

            RecreatePipelineIfNeeded(PipelineBindPoint.Graphics);
            BeginRenderPass();
            ResumeTransformFeedbackInternal();
            DrawCount++;

            if (_topology == GAL.PrimitiveTopology.Quads)
            {
                int quadsCount = vertexCount / 4;

                for (int i = 0; i < quadsCount; i++)
                {
                    Gd.Api.CmdDraw(CommandBuffer, 4, (uint)instanceCount, (uint)(firstVertex + i * 4), (uint)firstInstance);
                }
            }
            else
            {
                Gd.Api.CmdDraw(CommandBuffer, (uint)vertexCount, (uint)instanceCount, (uint)firstVertex, (uint)firstInstance);
            }
        }

        public void DrawIndexed(int indexCount, int instanceCount, int firstIndex, int firstVertex, int firstInstance)
        {
            // System.Console.WriteLine("draw indexed");

            if (!_program.IsLinked)
            {
                return;
            }

            RecreatePipelineIfNeeded(PipelineBindPoint.Graphics);
            BeginRenderPass();
            ResumeTransformFeedbackInternal();
            DrawCount++;

            if (_topology == GAL.PrimitiveTopology.Quads)
            {
                int quadsCount = indexCount / 4;

                for (int i = 0; i < quadsCount; i++)
                {
                    Gd.Api.CmdDrawIndexed(CommandBuffer, 4, (uint)instanceCount, (uint)(firstIndex + i * 4), firstVertex, (uint)firstInstance);
                }
            }
            else
            {
                Gd.Api.CmdDrawIndexed(CommandBuffer, (uint)indexCount, (uint)instanceCount, (uint)firstIndex, firstVertex, (uint)firstInstance);
            }
        }

        public void DrawTexture(ITexture texture, ISampler sampler, Extents2DF srcRegion, Extents2DF dstRegion)
        {
            if (texture is TextureView srcTexture)
            {
                SupportBufferUpdater.Commit();

                var oldCullMode = _newState.CullMode;
                var oldStencilTestEnable = _newState.StencilTestEnable;
                var oldDepthTestEnable = _newState.DepthTestEnable;
                var oldDepthWriteEnable = _newState.DepthWriteEnable;
                var oldTopology = _newState.Topology;
                var oldViewports = VulkanConfiguration.UseDynamicState ? _dynamicState.Viewports : _newState.Internal.Viewports;
                var oldViewportsCount = _newState.ViewportsCount;

                _newState.CullMode = CullModeFlags.CullModeNone;
                _newState.StencilTestEnable = false;
                _newState.DepthTestEnable = false;
                _newState.DepthWriteEnable = false;
                SignalStateChange();

                Gd.HelperShader.DrawTexture(
                    Gd,
                    this,
                    srcTexture,
                    sampler,
                    srcRegion,
                    dstRegion);

                _newState.CullMode = oldCullMode;
                _newState.StencilTestEnable = oldStencilTestEnable;
                _newState.DepthTestEnable = oldDepthTestEnable;
                _newState.DepthWriteEnable = oldDepthWriteEnable;
                _newState.Topology = oldTopology;

                if (VulkanConfiguration.UseDynamicState)
                {
                    _dynamicState.Viewports = oldViewports;
                    _dynamicState.ViewportsCount = (int)oldViewportsCount;
                    _dynamicState.SetViewportsDirty();
                }
                else
                {
                    _newState.Internal.Viewports = oldViewports;
                }

                _newState.ViewportsCount = oldViewportsCount;
                SignalStateChange();
            }
        }

        public void EndTransformFeedback()
        {
            PauseTransformFeedbackInternal();
            _tfEnabled = false;
        }

        public void MultiDrawIndirectCount(BufferRange indirectBuffer, BufferRange parameterBuffer, int maxDrawCount, int stride)
        {
            if (!Gd.SupportsIndirectParameters)
            {
                throw new NotSupportedException();
            }

            if (_program.LinkStatus != ProgramLinkStatus.Success)
            {
                return;
            }

            RecreatePipelineIfNeeded(PipelineBindPoint.Graphics);
            BeginRenderPass();
            ResumeTransformFeedbackInternal();
            DrawCount++;

            var buffer = Gd.BufferManager.GetBuffer(CommandBuffer, indirectBuffer.Handle, true).Get(Cbs, indirectBuffer.Offset, indirectBuffer.Size).Value;
            var countBuffer = Gd.BufferManager.GetBuffer(CommandBuffer, parameterBuffer.Handle, true).Get(Cbs, parameterBuffer.Offset, parameterBuffer.Size).Value;

            Gd.DrawIndirectCountApi.CmdDrawIndirectCount(
                CommandBuffer,
                buffer,
                (ulong)indirectBuffer.Offset,
                countBuffer,
                (ulong)parameterBuffer.Offset,
                (uint)maxDrawCount,
                (uint)stride);
        }

        public void MultiDrawIndexedIndirectCount(BufferRange indirectBuffer, BufferRange parameterBuffer, int maxDrawCount, int stride)
        {
            if (!Gd.SupportsIndirectParameters)
            {
                throw new NotSupportedException();
            }

            if (_program.LinkStatus != ProgramLinkStatus.Success)
            {
                return;
            }

            RecreatePipelineIfNeeded(PipelineBindPoint.Graphics);
            BeginRenderPass();
            ResumeTransformFeedbackInternal();
            DrawCount++;

            var buffer = Gd.BufferManager.GetBuffer(CommandBuffer, indirectBuffer.Handle, true).Get(Cbs, indirectBuffer.Offset, indirectBuffer.Size).Value;
            var countBuffer = Gd.BufferManager.GetBuffer(CommandBuffer, parameterBuffer.Handle, true).Get(Cbs, parameterBuffer.Offset, parameterBuffer.Size).Value;

            Gd.DrawIndirectCountApi.CmdDrawIndexedIndirectCount(
                CommandBuffer,
                buffer,
                (ulong)indirectBuffer.Offset,
                countBuffer,
                (ulong)parameterBuffer.Offset,
                (uint)maxDrawCount,
                (uint)stride);
        }

        public void SetAlphaTest(bool enable, float reference, GAL.CompareOp op)
        {
            // TODO.
        }

        public void SetBlendState(int index, BlendDescriptor blend)
        {
            ref var vkBlend = ref _newState.Internal.ColorBlendAttachmentState[index];

            vkBlend.BlendEnable = blend.Enable;
            vkBlend.SrcColorBlendFactor = blend.ColorSrcFactor.Convert();
            vkBlend.DstColorBlendFactor = blend.ColorDstFactor.Convert();
            vkBlend.ColorBlendOp = blend.ColorOp.Convert();
            vkBlend.SrcAlphaBlendFactor = blend.AlphaSrcFactor.Convert();
            vkBlend.DstAlphaBlendFactor = blend.AlphaDstFactor.Convert();
            vkBlend.AlphaBlendOp = blend.AlphaOp.Convert();

            _newState.BlendConstantR = blend.BlendConstant.Red;
            _newState.BlendConstantG = blend.BlendConstant.Green;
            _newState.BlendConstantB = blend.BlendConstant.Blue;
            _newState.BlendConstantA = blend.BlendConstant.Alpha;

            SignalStateChange();
        }

        public void SetDepthBias(PolygonModeMask enables, float factor, float units, float clamp)
        {
            if (VulkanConfiguration.UseDynamicState)
            {
                _dynamicState.SetDepthBias(factor, units, clamp);
            }
            else
            {
                _newState.DepthBiasSlopeFactor = factor;
                _newState.DepthBiasConstantFactor = units;
                _newState.DepthBiasClamp = clamp;
            }

            _newState.DepthBiasEnable = enables != 0;
            SignalStateChange();
        }

        public void SetDepthClamp(bool clamp)
        {
            _newState.DepthClampEnable = clamp;
            SignalStateChange();
        }

        public void SetDepthMode(DepthMode mode)
        {
            // TODO.
        }

        public void SetDepthTest(DepthTestDescriptor depthTest)
        {
            _newState.DepthTestEnable = depthTest.TestEnable;
            _newState.DepthWriteEnable = depthTest.WriteEnable;
            _newState.DepthCompareOp = depthTest.Func.Convert();
            SignalStateChange();
        }

        public void SetFaceCulling(bool enable, Face face)
        {
            _newState.CullMode = enable ? face.Convert() : CullModeFlags.CullModeNone;
            SignalStateChange();
        }

        public void SetFrontFace(GAL.FrontFace frontFace)
        {
            _newState.FrontFace = frontFace.Convert();
            SignalStateChange();
        }

        public void SetImage(int binding, ITexture image, GAL.Format imageFormat)
        {
            _descriptorSetUpdater.SetImage(binding, image, imageFormat);
        }

        public void SetIndexBuffer(BufferRange buffer, GAL.IndexType type)
        {
            _indexBuffer.Dispose();

            if (buffer.Handle != BufferHandle.Null)
            {
                Auto<DisposableBuffer> ib = null;
                int offset = buffer.Offset;
                int size = buffer.Size;

                if (type == GAL.IndexType.UByte && !Gd.SupportsIndexTypeUint8)
                {
                    ib = Gd.BufferManager.GetBufferI8ToI16(Cbs, buffer.Handle, offset, size);
                    offset = 0;
                    size *= 2;
                    type = GAL.IndexType.UShort;
                }
                else
                {
                    ib = Gd.BufferManager.GetBuffer(CommandBuffer, buffer.Handle, false);
                }

                _indexBuffer = new BufferState(ib, offset, size, type.Convert());
            }
            else
            {
                _indexBuffer = BufferState.Null;
            }

            _indexBuffer.BindIndexBuffer(Gd.Api, Cbs);
        }

        public void SetLineParameters(float width, bool smooth)
        {
            _newState.LineWidth = width;
            SignalStateChange();
        }

        public void SetLogicOpState(bool enable, LogicalOp op)
        {
            _newState.LogicOpEnable = enable;
            _newState.LogicOp = op.Convert();
            SignalStateChange();
        }

        public void SetOrigin(Origin origin)
        {
            // TODO.
        }

        public unsafe void SetPatchParameters(int vertices, ReadOnlySpan<float> defaultOuterLevel, ReadOnlySpan<float> defaultInnerLevel)
        {
            _newState.PatchControlPoints = (uint)vertices;
            SignalStateChange();

            // TODO: Default levels (likely needs emulation on shaders?)
        }

        public void SetPointParameters(float size, bool isProgramPointSize, bool enablePointSprite, Origin origin)
        {
            // TODO.
        }

        public void SetPolygonMode(GAL.PolygonMode frontMode, GAL.PolygonMode backMode)
        {
            // TODO.
        }

        public void SetPrimitiveRestart(bool enable, int index)
        {
            _newState.PrimitiveRestartEnable = enable;
            // TODO: What to do about the index?
            SignalStateChange();
        }

        public void SetPrimitiveTopology(GAL.PrimitiveTopology topology)
        {
            _topology = topology;

            var vkTopology = topology.Convert();

            _newState.Topology = vkTopology;

            SignalStateChange();
        }

        public void SetProgram(IProgram program)
        {
            var internalProgram = (ShaderCollection)program;
            var stages = internalProgram.GetInfos();

            _program = internalProgram;

            _descriptorSetUpdater.SetProgram(internalProgram);

            _newState.PipelineLayout = internalProgram.PipelineLayout;
            _newState.StagesCount = (uint)stages.Length;

            stages.CopyTo(_newState.Stages.ToSpan().Slice(0, stages.Length));

            SignalStateChange();
            SignalProgramChange();
        }

        protected virtual void SignalProgramChange()
        {
        }

        public void SetRasterizerDiscard(bool discard)
        {
            _newState.RasterizerDiscardEnable = discard;
            SignalStateChange();
        }

        public void SetRenderTargetColorMasks(ReadOnlySpan<uint> componentMask)
        {
            int count = Math.Min(Constants.MaxRenderTargets, componentMask.Length);

            for (int i = 0; i < count; i++)
            {
                ref var vkBlend = ref _newState.Internal.ColorBlendAttachmentState[i];

                vkBlend.ColorWriteMask = (ColorComponentFlags)componentMask[i];
            }

            SignalStateChange();
        }

        public void SetRenderTargets(ITexture[] colors, ITexture depthStencil)
        {
            CreateFramebuffer(colors, depthStencil);
            CreateRenderPass();
            SignalStateChange();
        }

        public void SetRenderTargetScale(float scale)
        {
            _renderScale[0].X = scale;
            SupportBufferUpdater.UpdateRenderScale(_renderScale, 0, 1); // Just the first element.
        }

        public void SetScissors(ReadOnlySpan<Rectangle<int>> regions)
        {
            int count = Math.Min(Constants.MaxViewports, regions.Length);

            for (int i = 0; i < count; i++)
            {
                var region = regions[i];
                var offset = new Offset2D(region.X, region.Y);
                var extent = new Extent2D((uint)region.Width, (uint)region.Height);

                if (VulkanConfiguration.UseDynamicState)
                {
                    _dynamicState.SetScissor(i, new Rect2D(offset, extent));
                }
                else
                {
                    _newState.Internal.Scissors[i] = new Rect2D(offset, extent);
                }
            }

            if (VulkanConfiguration.UseDynamicState)
            {
                _dynamicState.ScissorsCount = count;
            }

            _newState.ScissorsCount = (uint)count;
            SignalStateChange();
        }

        public void SetStencilTest(StencilTestDescriptor stencilTest)
        {
            if (VulkanConfiguration.UseDynamicState)
            {
                _dynamicState.SetStencilMasks(
                    (uint)stencilTest.BackFuncMask,
                    (uint)stencilTest.BackMask,
                    (uint)stencilTest.BackFuncRef,
                    (uint)stencilTest.FrontFuncMask,
                    (uint)stencilTest.FrontMask,
                    (uint)stencilTest.FrontFuncRef);
            }
            else
            {
                _newState.StencilBackCompareMask = (uint)stencilTest.BackFuncMask;
                _newState.StencilBackWriteMask = (uint)stencilTest.BackMask;
                _newState.StencilBackReference = (uint)stencilTest.BackFuncRef;
                _newState.StencilFrontCompareMask = (uint)stencilTest.FrontFuncMask;
                _newState.StencilFrontWriteMask = (uint)stencilTest.FrontMask;
                _newState.StencilFrontReference = (uint)stencilTest.FrontFuncRef;
            }

            _newState.StencilTestEnable = stencilTest.TestEnable;
            _newState.StencilBackFailOp = stencilTest.BackSFail.Convert();
            _newState.StencilBackPassOp = stencilTest.BackDpPass.Convert();
            _newState.StencilBackDepthFailOp = stencilTest.BackDpFail.Convert();
            _newState.StencilBackCompareOp = stencilTest.BackFunc.Convert();
            _newState.StencilFrontFailOp = stencilTest.FrontSFail.Convert();
            _newState.StencilFrontPassOp = stencilTest.FrontDpPass.Convert();
            _newState.StencilFrontDepthFailOp = stencilTest.FrontDpFail.Convert();
            _newState.StencilFrontCompareOp = stencilTest.FrontFunc.Convert();
            SignalStateChange();
        }

        public void SetStorageBuffers(int first, ReadOnlySpan<BufferRange> buffers)
        {
            _descriptorSetUpdater.SetStorageBuffers(CommandBuffer, first, buffers);
        }

        public void SetTextureAndSampler(int binding, ITexture texture, ISampler sampler)
        {
            _descriptorSetUpdater.SetTextureAndSampler(binding, texture, sampler);
        }

        public void SetTransformFeedbackBuffers(ReadOnlySpan<BufferRange> buffers)
        {
            PauseTransformFeedbackInternal();

            int count = Math.Min(Constants.MaxTransformFeedbackBuffers, buffers.Length);

            for (int i = 0; i < count; i++)
            {
                var range = buffers[i];

                _transformFeedbackBuffers[i].Dispose();

                if (range.Handle != BufferHandle.Null)
                {
                    _transformFeedbackBuffers[i] = new BufferState(Gd.BufferManager.GetBuffer(CommandBuffer, range.Handle, true), range.Offset, range.Size);
                    _transformFeedbackBuffers[i].BindTransformFeedbackBuffer(Gd, Cbs, (uint)i);
                }
                else
                {
                    _transformFeedbackBuffers[i] = BufferState.Null;
                }
            }
        }

        public void SetUniformBuffers(int first, ReadOnlySpan<BufferRange> buffers)
        {
            _descriptorSetUpdater.SetUniformBuffers(CommandBuffer, first, buffers);
        }

        public void SetUserClipDistance(int index, bool enableClip)
        {
            // TODO.
        }

        public void SetVertexAttribs(ReadOnlySpan<VertexAttribDescriptor> vertexAttribs)
        {
            int count = Math.Min(Constants.MaxVertexAttributes, vertexAttribs.Length);

            for (int i = 0; i < count; i++)
            {
                var attribute = vertexAttribs[i];
                var bufferIndex = attribute.IsZero ? 0 : attribute.BufferIndex + 1;

                _newState.Internal.VertexAttributeDescriptions[i] = new VertexInputAttributeDescription(
                    (uint)i,
                    (uint)bufferIndex,
                    FormatTable.GetFormat(attribute.Format),
                    (uint)attribute.Offset);
            }

            _newState.VertexAttributeDescriptionsCount = (uint)count;
            SignalStateChange();
        }

        public void SetVertexBuffers(ReadOnlySpan<VertexBufferDescriptor> vertexBuffers)
        {
            int count = Math.Min(Constants.MaxVertexBuffers, vertexBuffers.Length);

            _newState.Internal.VertexBindingDescriptions[0] = new VertexInputBindingDescription(0, 0, VertexInputRate.Vertex);

            int validCount = 1;

            for (int i = 0; i < count; i++)
            {
                var vertexBuffer = vertexBuffers[i];

                // TODO: Support divisor > 1
                var inputRate = vertexBuffer.Divisor != 0 ? VertexInputRate.Instance : VertexInputRate.Vertex;

                if (vertexBuffer.Buffer.Handle != BufferHandle.Null)
                {
                    var vb = Gd.BufferManager.GetBuffer(CommandBuffer, vertexBuffer.Buffer.Handle, false);
                    if (vb != null)
                    {
                        int binding = i + 1;
                        int descriptorIndex = validCount++;

                        _newState.Internal.VertexBindingDescriptions[descriptorIndex] = new VertexInputBindingDescription(
                            (uint)binding,
                            (uint)vertexBuffer.Stride,
                            inputRate);

                        int vbSize = vertexBuffer.Buffer.Size;

                        if (Gd.Vendor == Vendor.Amd && vertexBuffer.Stride > 0)
                        {
                            // AMD has a bug where if offset + stride * count is greater than
                            // the size, then the last attribute will have the wrong value.
                            // As a workaround, simply use the full buffer size.
                            int remainder = vbSize % vertexBuffer.Stride;
                            if (remainder != 0)
                            {
                                vbSize += vertexBuffer.Stride - remainder;
                            }
                        }

                        _vertexBuffers[binding].Dispose();
                        _vertexBuffers[binding] = new BufferState(
                            vb,
                            vertexBuffer.Buffer.Offset,
                            vbSize,
                            (ulong)vertexBuffer.Stride);

                        _vertexBuffers[binding].BindVertexBuffer(Gd, Cbs, (uint)binding);
                    }
                }
            }

            _newState.VertexBindingDescriptionsCount = (uint)validCount;
            SignalStateChange();
        }

        // TODO: Remove first parameter.
        public void SetViewports(int first, ReadOnlySpan<GAL.Viewport> viewports)
        {
            int count = Math.Min(Constants.MaxViewports, viewports.Length);

            static float Clamp(float value)
            {
                return Math.Clamp(value, 0f, 1f);
            }

            if (VulkanConfiguration.UseDynamicState)
            {
                for (int i = 0; i < count; i++)
                {
                    var viewport = viewports[i];

                    _dynamicState.SetViewport(i, new Silk.NET.Vulkan.Viewport(
                        viewport.Region.X,
                        viewport.Region.Y,
                        viewport.Region.Width == 0f ? 1f : viewport.Region.Width,
                        viewport.Region.Height == 0f ? 1f : viewport.Region.Height,
                        Clamp(viewport.DepthNear),
                        Clamp(viewport.DepthFar)));
                }

                _dynamicState.ViewportsCount = count;
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    var viewport = viewports[i];

                    ref var vkViewport = ref _newState.Internal.Viewports[i];

                    vkViewport.X = viewport.Region.X;
                    vkViewport.Y = viewport.Region.Y;
                    vkViewport.Width = viewport.Region.Width == 0f ? 1f : viewport.Region.Width;
                    vkViewport.Height = viewport.Region.Height == 0f ? 1f : viewport.Region.Height;
                    vkViewport.MinDepth = Clamp(viewport.DepthNear);
                    vkViewport.MaxDepth = Clamp(viewport.DepthFar);
                }
            }

            _newState.ViewportsCount = (uint)count;
            SignalStateChange();
        }

        public unsafe void TextureBarrier()
        {
            MemoryBarrier memoryBarrier = new MemoryBarrier()
            {
                SType = StructureType.MemoryBarrier,
                SrcAccessMask = AccessFlags.AccessMemoryReadBit | AccessFlags.AccessMemoryWriteBit,
                DstAccessMask = AccessFlags.AccessMemoryReadBit | AccessFlags.AccessMemoryWriteBit
            };

            Gd.Api.CmdPipelineBarrier(
                CommandBuffer,
                PipelineStageFlags.PipelineStageFragmentShaderBit,
                PipelineStageFlags.PipelineStageFragmentShaderBit,
                0,
                1,
                memoryBarrier,
                0,
                null,
                0,
                null);
        }

        public void TextureBarrierTiled()
        {
            TextureBarrier();
        }

        public void UpdateRenderScale(ReadOnlySpan<float> scales, int totalCount, int fragmentCount)
        {
            bool changed = false;

            for (int index = 0; index < totalCount; index++)
            {
                if (_renderScale[1 + index].X != scales[index])
                {
                    _renderScale[1 + index].X = scales[index];
                    changed = true;
                }
            }

            // Only update fragment count if there are scales after it for the vertex stage.
            if (fragmentCount != totalCount && fragmentCount != _fragmentScaleCount)
            {
                _fragmentScaleCount = fragmentCount;
                SupportBufferUpdater.UpdateFragmentRenderScaleCount(_fragmentScaleCount);
            }

            if (changed)
            {
                SupportBufferUpdater.UpdateRenderScale(_renderScale, 0, 1 + totalCount);
            }
        }

        protected void SignalCommandBufferChange()
        {
            _needsIndexBufferRebind = true;
            _needsTransformFeedbackBuffersRebind = true;
            _needsVertexBuffersRebind = true;

            _descriptorSetUpdater.SignalCommandBufferChange();
            _dynamicState.ForceAllDirty();
            _currentPipelineHandle = 0;
        }

        private void CreateFramebuffer(ITexture[] colors, ITexture depthStencil)
        {
            FramebufferParams = new FramebufferParams(Device, colors, depthStencil);
            UpdatePipelineAttachmentFormats();
        }

        protected void UpdatePipelineAttachmentFormats()
        {
            var dstAttachmentFormats = _newState.Internal.AttachmentFormats.ToSpan();
            FramebufferParams.AttachmentFormats.CopyTo(dstAttachmentFormats);

            int maxAttachmentIndex = FramebufferParams.MaxColorAttachmentIndex + (FramebufferParams.HasDepthStencil ? 1 : 0);
            for (int i = FramebufferParams.AttachmentFormats.Length; i <= maxAttachmentIndex; i++)
            {
                dstAttachmentFormats[i] = 0;
            }

            _newState.ColorBlendAttachmentStateCount = (uint)(FramebufferParams.MaxColorAttachmentIndex + 1);
            _newState.HasDepthStencil = FramebufferParams.HasDepthStencil;
        }

        protected unsafe void CreateRenderPass()
        {
            const int MaxAttachments = Constants.MaxRenderTargets + 1;

            AttachmentDescription[] attachmentDescs = null;

            var subpass = new SubpassDescription()
            {
                PipelineBindPoint = PipelineBindPoint.Graphics
            };

            AttachmentReference* attachmentReferences = stackalloc AttachmentReference[MaxAttachments];

            var hasFramebuffer = FramebufferParams != null;

            if (hasFramebuffer && FramebufferParams.AttachmentsCount != 0)
            {
                attachmentDescs = new AttachmentDescription[FramebufferParams.AttachmentsCount];

                for (int i = 0; i < FramebufferParams.AttachmentsCount; i++)
                {
                    int bindIndex = FramebufferParams.AttachmentIndices[i];

                    attachmentDescs[i] = new AttachmentDescription(
                        0,
                        FramebufferParams.AttachmentFormats[i],
                        SampleCountFlags.SampleCount1Bit,
                        AttachmentLoadOp.Load,
                        AttachmentStoreOp.Store,
                        AttachmentLoadOp.Load,
                        AttachmentStoreOp.Store,
                        ImageLayout.General,
                        ImageLayout.General);
                }

                int colorAttachmentsCount = FramebufferParams.ColorAttachmentsCount;

                if (colorAttachmentsCount > MaxAttachments - 1)
                {
                    colorAttachmentsCount = MaxAttachments - 1;
                }

                if (colorAttachmentsCount != 0)
                {
                    int maxAttachmentIndex = FramebufferParams.MaxColorAttachmentIndex;
                    subpass.ColorAttachmentCount = (uint)maxAttachmentIndex + 1;
                    subpass.PColorAttachments = &attachmentReferences[0];

                    // Fill with VK_ATTACHMENT_UNUSED to cover any gaps.
                    for (int i = 0; i <= maxAttachmentIndex; i++)
                    {
                        subpass.PColorAttachments[i] = new AttachmentReference(Vk.AttachmentUnused, ImageLayout.Undefined);
                    }

                    for (int i = 0; i < colorAttachmentsCount; i++)
                    {
                        int bindIndex = FramebufferParams.AttachmentIndices[i];

                        subpass.PColorAttachments[bindIndex] = new AttachmentReference((uint)i, ImageLayout.General);
                    }
                }

                if (FramebufferParams.HasDepthStencil)
                {
                    uint dsIndex = (uint)FramebufferParams.AttachmentsCount - 1;

                    subpass.PDepthStencilAttachment = &attachmentReferences[MaxAttachments - 1];
                    *subpass.PDepthStencilAttachment = new AttachmentReference(dsIndex, ImageLayout.General);
                }
            }

            var subpassDependency = new SubpassDependency(
                0,
                0,
                PipelineStageFlags.PipelineStageAllGraphicsBit,
                PipelineStageFlags.PipelineStageAllGraphicsBit,
                AccessFlags.AccessMemoryReadBit | AccessFlags.AccessMemoryWriteBit,
                AccessFlags.AccessMemoryReadBit | AccessFlags.AccessMemoryWriteBit,
                0);

            fixed (AttachmentDescription* pAttachmentDescs = attachmentDescs)
            {
                var renderPassCreateInfo = new RenderPassCreateInfo()
                {
                    SType = StructureType.RenderPassCreateInfo,
                    PAttachments = pAttachmentDescs,
                    AttachmentCount = attachmentDescs != null ? (uint)attachmentDescs.Length : 0,
                    PSubpasses = &subpass,
                    SubpassCount = 1,
                    PDependencies = &subpassDependency,
                    DependencyCount = 1
                };

                Gd.Api.CreateRenderPass(Device, renderPassCreateInfo, null, out var renderPass).ThrowOnError();

                _renderPass?.Dispose();
                _renderPass = new Auto<DisposableRenderPass>(new DisposableRenderPass(Gd.Api, Device, renderPass));
            }

            EndRenderPass();

            _framebuffer?.Dispose();
            _framebuffer = hasFramebuffer ? FramebufferParams.Create(Gd.Api, Cbs, _renderPass) : null;
        }

        protected void SignalStateChange([System.Runtime.CompilerServices.CallerMemberName] string caller = "")
        {
            // System.Console.WriteLine("state change by " + caller);
            _stateDirty = true;
        }

        private void RecreatePipelineIfNeeded(PipelineBindPoint pbp)
        {
            // Take the opportunity to process any pending work requested by other threads.
            _dynamicState.ReplayIfDirty(Gd.Api, CommandBuffer);

            // Commit changes to the support buffer before drawing.
            SupportBufferUpdater.Commit();

            if (_stateDirty || Pbp != pbp)
            {
                CreatePipeline(pbp);
                _stateDirty = false;
                Pbp = pbp;
            }

            if (_needsIndexBufferRebind)
            {
                _indexBuffer.BindIndexBuffer(Gd.Api, Cbs);
                _needsIndexBufferRebind = false;
            }

            if (_needsTransformFeedbackBuffersRebind)
            {
                PauseTransformFeedbackInternal();

                for (int i = 0; i < Constants.MaxTransformFeedbackBuffers; i++)
                {
                    _transformFeedbackBuffers[i].BindTransformFeedbackBuffer(Gd, Cbs, (uint)i);
                }

                _needsTransformFeedbackBuffersRebind = false;
            }

            if (_needsVertexBuffersRebind)
            {
                for (int i = 0; i < Constants.MaxVertexBuffers + 1; i++)
                {
                    _vertexBuffers[i].BindVertexBuffer(Gd, Cbs, (uint)i);
                }

                _needsVertexBuffersRebind = false;
            }

            _descriptorSetUpdater.UpdateAndBindDescriptorSets(Cbs, pbp);
        }

        private void CreatePipeline(PipelineBindPoint pbp)
        {
            // We can only create a pipeline if the have the shader stages set.
            if (_newState.Stages != null)
            {
                if (pbp == PipelineBindPoint.Graphics && _renderPass == null)
                {
                    CreateRenderPass();
                }

                var pipeline = pbp == PipelineBindPoint.Compute
                    ? _newState.CreateComputePipeline(Gd.Api, Device, _program, _pipelineCache)
                    : _newState.CreateGraphicsPipeline(Gd, Device, _program, _pipelineCache, _renderPass.Get(Cbs).Value);

                ulong pipelineHandle = pipeline.GetUnsafe().Value.Handle;

                if (_currentPipelineHandle != pipelineHandle)
                {
                    _currentPipelineHandle = pipelineHandle;
                    // _pipeline?.Dispose();
                    Pipeline = pipeline;

                    PauseTransformFeedbackInternal();
                    Gd.Api.CmdBindPipeline(CommandBuffer, pbp, Pipeline.Get(Cbs).Value);
                }
            }
        }

        private unsafe void BeginRenderPass()
        {
            if (!_renderPassActive)
            {
                var renderArea = new Rect2D(null, new Extent2D(FramebufferParams.Width, FramebufferParams.Height));
                var clearValue = new ClearValue();

                var renderPassBeginInfo = new RenderPassBeginInfo()
                {
                    SType = StructureType.RenderPassBeginInfo,
                    RenderPass = _renderPass.Get(Cbs).Value,
                    Framebuffer = _framebuffer.Get(Cbs).Value,
                    RenderArea = renderArea,
                    PClearValues = &clearValue,
                    ClearValueCount = 1
                };

                Gd.Api.CmdBeginRenderPass(CommandBuffer, renderPassBeginInfo, SubpassContents.Inline);
                _renderPassActive = true;
            }
        }

        public void EndRenderPass()
        {
            if (_renderPassActive)
            {
                PauseTransformFeedbackInternal();
                // System.Console.WriteLine("render pass ended " + caller);
                Gd.Api.CmdEndRenderPass(CommandBuffer);
                _renderPassActive = false;
            }
        }

        private void PauseTransformFeedbackInternal()
        {
            if (_tfEnabled && _tfActive)
            {
                EndTransformFeedbackInternal();
                _tfActive = false;
            }
        }

        private void ResumeTransformFeedbackInternal()
        {
            if (_tfEnabled && !_tfActive)
            {
                BeginTransformFeedbackInternal();
                _tfActive = true;
            }
        }

        private unsafe void BeginTransformFeedbackInternal()
        {
            Gd.TransformFeedbackApi.CmdBeginTransformFeedback(CommandBuffer, 0, 0, null, null);
        }

        private unsafe void EndTransformFeedbackInternal()
        {
            Gd.TransformFeedbackApi.CmdEndTransformFeedback(CommandBuffer, 0, 0, null, null);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _renderPass?.Dispose();
                _framebuffer?.Dispose();
                _indexBuffer.Dispose();
                _newState.Dispose();
                _descriptorSetUpdater.Dispose();

                for (int i = 0; i < _vertexBuffers.Length; i++)
                {
                    _vertexBuffers[i].Dispose();
                }

                for (int i = 0; i < _transformFeedbackBuffers.Length; i++)
                {
                    _transformFeedbackBuffers[i].Dispose();
                }

                Pipeline?.Dispose();

                unsafe
                {
                    Gd.Api.DestroyPipelineCache(Device, _pipelineCache, null);
                }

                SupportBufferUpdater.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
