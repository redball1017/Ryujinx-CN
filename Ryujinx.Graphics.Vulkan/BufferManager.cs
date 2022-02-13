﻿using Ryujinx.Graphics.GAL;
using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VkFormat = Silk.NET.Vulkan.Format;

namespace Ryujinx.Graphics.Vulkan
{
    class BufferManager : IDisposable
    {
        private const MemoryPropertyFlags DefaultBufferMemoryFlags =
            MemoryPropertyFlags.MemoryPropertyHostVisibleBit |
            MemoryPropertyFlags.MemoryPropertyHostCoherentBit |
            MemoryPropertyFlags.MemoryPropertyHostCachedBit;

        private const MemoryPropertyFlags DeviceLocalBufferMemoryFlags =
            MemoryPropertyFlags.MemoryPropertyDeviceLocalBit;

        private const MemoryPropertyFlags FlushableDeviceLocalBufferMemoryFlags =
            MemoryPropertyFlags.MemoryPropertyHostVisibleBit |
            MemoryPropertyFlags.MemoryPropertyHostCoherentBit |
            MemoryPropertyFlags.MemoryPropertyDeviceLocalBit;

        private const BufferUsageFlags DefaultBufferUsageFlags =
            BufferUsageFlags.BufferUsageTransferSrcBit |
            BufferUsageFlags.BufferUsageTransferDstBit |
            BufferUsageFlags.BufferUsageUniformTexelBufferBit |
            BufferUsageFlags.BufferUsageStorageTexelBufferBit |
            BufferUsageFlags.BufferUsageUniformBufferBit |
            BufferUsageFlags.BufferUsageStorageBufferBit |
            BufferUsageFlags.BufferUsageIndexBufferBit |
            BufferUsageFlags.BufferUsageVertexBufferBit |
            BufferUsageFlags.BufferUsageTransformFeedbackBufferBitExt;

        private readonly PhysicalDevice _physicalDevice;
        private readonly Device _device;

        private readonly List<BufferHolder> _buffers;

        public StagingBuffer StagingBuffer { get; }

        public BufferManager(VulkanGraphicsDevice gd, PhysicalDevice physicalDevice, Device device)
        {
            _physicalDevice = physicalDevice;
            _device = device;
            _buffers = new List<BufferHolder>();
            StagingBuffer = new StagingBuffer(gd, this);
        }

        public BufferHandle CreateWithHandle(VulkanGraphicsDevice gd, int size, bool deviceLocal)
        {
            var holder = Create(gd, size, deviceLocal: deviceLocal);
            if (holder == null)
            {
                return BufferHandle.Null;
            }

            ulong handle64 = (ulong)_buffers.Count + 1;

            var handle = Unsafe.As<ulong, BufferHandle>(ref handle64);

            _buffers.Add(holder);

            return handle;
        }

        public unsafe BufferHolder Create(VulkanGraphicsDevice gd, int size, bool forConditionalRendering = false, bool deviceLocal = false)
        {
            var usage = DefaultBufferUsageFlags;

            if (forConditionalRendering && gd.Capabilities.SupportsConditionalRendering)
            {
                usage |= BufferUsageFlags.BufferUsageConditionalRenderingBitExt;
            }
            else if (gd.SupportsIndirectParameters)
            {
                usage |= BufferUsageFlags.BufferUsageIndirectBufferBit;
            }

            var bufferCreateInfo = new BufferCreateInfo()
            {
                SType = StructureType.BufferCreateInfo,
                Size = (ulong)size,
                Usage = usage,
                SharingMode = SharingMode.Exclusive
            };

            gd.Api.CreateBuffer(_device, in bufferCreateInfo, null, out var buffer).ThrowOnError();
            gd.Api.GetBufferMemoryRequirements(_device, buffer, out var requirements);

            var allocateFlags = deviceLocal ? DeviceLocalBufferMemoryFlags : DefaultBufferMemoryFlags;

            var allocation = gd.MemoryAllocator.AllocateDeviceMemory(_physicalDevice, requirements, allocateFlags);

            if (allocation.Memory.Handle == 0UL)
            {
                gd.Api.DestroyBuffer(_device, buffer, null);
                return null;
            }

            gd.Api.BindBufferMemory(_device, buffer, allocation.Memory, allocation.Offset);

            return new BufferHolder(gd, _device, buffer, allocation, size);
        }

        public Auto<DisposableBufferView> CreateView(BufferHandle handle, VkFormat format, int offset, int size)
        {
            if (TryGetBuffer(handle, out var holder))
            {
                return holder.CreateView(format, offset, size);
            }

            return null;
        }

        public Auto<DisposableBuffer> GetBuffer(CommandBuffer commandBuffer, BufferHandle handle, bool isWrite)
        {
            if (TryGetBuffer(handle, out var holder))
            {
                return holder.GetBuffer(commandBuffer, isWrite);
            }

            return null;
        }

        public Auto<DisposableBuffer> GetBufferI8ToI16(CommandBufferScoped cbs, BufferHandle handle, int offset, int size)
        {
            if (TryGetBuffer(handle, out var holder))
            {
                return holder.GetBufferI8ToI16(cbs, offset, size);
            }

            return null;
        }

        public Auto<DisposableBuffer> GetBuffer(CommandBuffer commandBuffer, BufferHandle handle, bool isWrite, out int size)
        {
            if (TryGetBuffer(handle, out var holder))
            {
                size = holder.Size;
                return holder.GetBuffer(commandBuffer, isWrite);
            }

            size = 0;
            return null;
        }

        public ReadOnlySpan<byte> GetData(BufferHandle handle, int offset, int size)
        {
            if (TryGetBuffer(handle, out var holder))
            {
                return holder.GetData(offset, size);
            }

            return ReadOnlySpan<byte>.Empty;
        }

        public void SetData<T>(BufferHandle handle, int offset, ReadOnlySpan<T> data) where T : unmanaged
        {
            SetData(handle, offset, MemoryMarshal.Cast<T, byte>(data), null, null);
        }

        public void SetData(BufferHandle handle, int offset, ReadOnlySpan<byte> data, CommandBufferScoped? cbs, Action endRenderPass)
        {
            if (TryGetBuffer(handle, out var holder))
            {
                holder.SetData(offset, data, cbs, endRenderPass);
            }
        }

        public void Delete(BufferHandle handle)
        {
            if (TryGetBuffer(handle, out var holder))
            {
                holder.Dispose();
                // _buffers.Remove(handle);
            }
        }

        private bool TryGetBuffer(BufferHandle handle, out BufferHolder holder)
        {
            int index = (int)Unsafe.As<BufferHandle, ulong>(ref handle) - 1;
            if ((uint)index < _buffers.Count)
            {
                holder = _buffers[index];
                return true;
            }

            holder = default;
            return false;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                for (int i = 0; i < _buffers.Count; i++)
                {
                    _buffers[i].Dispose();
                }

                StagingBuffer.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
