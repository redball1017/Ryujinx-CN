﻿using Silk.NET.Vulkan;
using System;

namespace Ryujinx.Graphics.Vulkan
{
    struct DisposableMemory : IDisposable
    {
        private readonly Vk _api;
        private readonly Device _device;
        private readonly DeviceMemory _memory;

        public DisposableMemory(Vk api, Device device, DeviceMemory memory)
        {
            _api = api;
            _device = device;
            _memory = memory;
        }

        public unsafe void Dispose()
        {
            _api.FreeMemory(_device, _memory, null);
        }
    }
}
