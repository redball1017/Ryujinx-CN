﻿using Silk.NET.Vulkan;
using System;

namespace Ryujinx.Graphics.Vulkan
{
    struct MemoryAllocation : IDisposable
    {
        private readonly MemoryAllocatorBlockList _owner;
        private readonly MemoryAllocatorBlockList.Block _block;

        public DeviceMemory Memory { get; }
        public IntPtr HostPointer { get;}
        public ulong Offset { get; }
        public ulong Size { get; }

        public MemoryAllocation(
            MemoryAllocatorBlockList owner,
            MemoryAllocatorBlockList.Block block,
            DeviceMemory memory,
            IntPtr hostPointer,
            ulong offset,
            ulong size)
        {
            _owner = owner;
            _block = block;
            Memory = memory;
            HostPointer = hostPointer;
            Offset = offset;
            Size = size;
        }

        public void Dispose()
        {
            _owner.Free(_block, Offset, Size);
        }
    }
}
