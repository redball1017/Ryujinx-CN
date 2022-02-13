﻿using Ryujinx.Common.Memory;
using Silk.NET.Vulkan;

namespace Ryujinx.Graphics.Vulkan
{
    struct PipelineDynamicState
    {
        private float _depthBiasSlopeFactor;
        private float _depthBiasConstantFactor;
        private float _depthBiasClamp;

        public int ScissorsCount;
        private Array16<Rect2D> _scissors;

        private uint _backCompareMask;
        private uint _backWriteMask;
        private uint _backReference;
        private uint _frontCompareMask;
        private uint _frontWriteMask;
        private uint _frontReference;

        public int ViewportsCount;
        public Array16<Viewport> Viewports;

        private enum DirtyFlags
        {
            None = 0,
            DepthBias = 1 << 0,
            Scissor = 1 << 1,
            Stencil = 1 << 2,
            Viewport = 1 << 3,
            All = DepthBias | Scissor | Stencil | Viewport
        }

        private DirtyFlags _dirty;

        public void SetDepthBias(float slopeFactor, float constantFactor, float clamp)
        {
            _depthBiasSlopeFactor = slopeFactor;
            _depthBiasConstantFactor = constantFactor;
            _depthBiasClamp = clamp;

            _dirty |= DirtyFlags.DepthBias;
        }

        public void SetScissor(int index, Rect2D scissor)
        {
            _scissors[index] = scissor;

            _dirty |= DirtyFlags.Scissor;
        }

        public void SetStencilMasks(
            uint backCompareMask,
            uint backWriteMask,
            uint backReference,
            uint frontCompareMask,
            uint frontWriteMask,
            uint frontReference)
        {
            _backCompareMask = backCompareMask;
            _backWriteMask = backWriteMask;
            _backReference = backReference;
            _frontCompareMask = frontCompareMask;
            _frontWriteMask = frontWriteMask;
            _frontReference = frontReference;

            _dirty |= DirtyFlags.Stencil;
        }

        public void SetViewport(int index, Viewport viewport)
        {
            Viewports[index] = viewport;

            _dirty |= DirtyFlags.Viewport;
        }

        public void SetViewportsDirty()
        {
            _dirty |= DirtyFlags.Viewport;
        }

        public void ForceAllDirty()
        {
            _dirty = DirtyFlags.All;
        }

        public void ReplayIfDirty(Vk api, CommandBuffer commandBuffer)
        {
            if (_dirty.HasFlag(DirtyFlags.DepthBias))
            {
                RecordDepthBias(api, commandBuffer);
            }

            if (_dirty.HasFlag(DirtyFlags.Scissor))
            {
                RecordScissor(api, commandBuffer);
            }

            if (_dirty.HasFlag(DirtyFlags.Stencil))
            {
                RecordStencilMasks(api, commandBuffer);
            }

            if (_dirty.HasFlag(DirtyFlags.Viewport))
            {
                RecordViewport(api, commandBuffer);
            }

            _dirty = DirtyFlags.None;
        }

        private void RecordDepthBias(Vk api, CommandBuffer commandBuffer)
        {
            api.CmdSetDepthBias(commandBuffer, _depthBiasConstantFactor, _depthBiasClamp, _depthBiasSlopeFactor);
        }

        private void RecordScissor(Vk api, CommandBuffer commandBuffer)
        {
            api.CmdSetScissor(commandBuffer, 0, (uint)ScissorsCount, _scissors.ToSpan());
        }

        private void RecordStencilMasks(Vk api, CommandBuffer commandBuffer)
        {
            api.CmdSetStencilCompareMask(commandBuffer, StencilFaceFlags.StencilFaceBackBit, _backCompareMask);
            api.CmdSetStencilWriteMask(commandBuffer, StencilFaceFlags.StencilFaceBackBit, _backWriteMask);
            api.CmdSetStencilReference(commandBuffer, StencilFaceFlags.StencilFaceBackBit, _backReference);
            api.CmdSetStencilCompareMask(commandBuffer, StencilFaceFlags.StencilFaceFrontBit, _frontCompareMask);
            api.CmdSetStencilWriteMask(commandBuffer, StencilFaceFlags.StencilFaceFrontBit, _frontWriteMask);
            api.CmdSetStencilReference(commandBuffer, StencilFaceFlags.StencilFaceFrontBit, _frontReference);
        }

        private void RecordViewport(Vk api, CommandBuffer commandBuffer)
        {
            api.CmdSetViewport(commandBuffer, 0, (uint)ViewportsCount, Viewports.ToSpan());
        }
    }
}
