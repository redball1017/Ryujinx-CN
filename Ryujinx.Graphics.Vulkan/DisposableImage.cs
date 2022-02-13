﻿using Silk.NET.Vulkan;
using System;

namespace Ryujinx.Graphics.Vulkan
{
    struct DisposableImage : IDisposable
    {
        private readonly Vk _api;
        private readonly Device _device;

        public Image Value { get; }

        public DisposableImage(Vk api, Device device, Image image)
        {
            _api = api;
            _device = device;
            Value = image;
        }

        public unsafe void Dispose()
        {
            _api.DestroyImage(_device, Value, null);
        }
    }
}
