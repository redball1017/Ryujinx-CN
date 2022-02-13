﻿using Ryujinx.Graphics.GAL;
using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;
using VkFormat = Silk.NET.Vulkan.Format;

namespace Ryujinx.Graphics.Vulkan
{
    class TextureBuffer : ITexture
    {
        private readonly VulkanGraphicsDevice _gd;

        private BufferHandle _bufferHandle;
        private int _offset;
        private int _size;
        private Auto<DisposableBufferView> _bufferView;
        private Dictionary<GAL.Format, Auto<DisposableBufferView>> _selfManagedViews;

        public int Width { get; }
        public int Height { get; }

        public VkFormat VkFormat { get; }

        public float ScaleFactor { get; }

        public TextureBuffer(VulkanGraphicsDevice gd, TextureCreateInfo info, float scale)
        {
            _gd = gd;
            Width = info.Width;
            Height = info.Height;
            VkFormat = FormatTable.GetFormat(info.Format);
            ScaleFactor = scale;

            gd.Textures.Add(this);
        }

        public void CopyTo(ITexture destination, int firstLayer, int firstLevel)
        {
            throw new NotSupportedException();
        }

        public void CopyTo(ITexture destination, int srcLayer, int dstLayer, int srcLevel, int dstLevel)
        {
            throw new NotSupportedException();
        }

        public void CopyTo(ITexture destination, Extents2D srcRegion, Extents2D dstRegion, bool linearFilter)
        {
            throw new NotSupportedException();
        }

        public ITexture CreateView(TextureCreateInfo info, int firstLayer, int firstLevel)
        {
            throw new NotSupportedException();
        }

        public ReadOnlySpan<byte> GetData()
        {
            return _gd.GetBufferData(_bufferHandle, _offset, _size);
        }

        public ReadOnlySpan<byte> GetData(int layer, int level)
        {
            return GetData();
        }

        public void Release()
        {
            if (_gd.Textures.Remove(this))
            {
                ReleaseImpl();
            }
        }

        private void ReleaseImpl()
        {
            if (_selfManagedViews != null)
            {
                foreach (var bufferView in _selfManagedViews.Values)
                {
                    bufferView.Dispose();
                }

                _selfManagedViews = null;
            }

            _bufferView?.Dispose();
            _bufferView = null;
        }

        public void SetData(ReadOnlySpan<byte> data)
        {
            _gd.SetBufferData(_bufferHandle, _offset, data);
        }

        public void SetData(ReadOnlySpan<byte> data, int layer, int level)
        {
            throw new NotSupportedException();
        }

        public void SetStorage(BufferRange buffer)
        {
            if (_bufferHandle == buffer.Handle &&
                _offset == buffer.Offset &&
                _size == buffer.Size)
            {
                return;
            }

            _bufferHandle = buffer.Handle;
            _offset = buffer.Offset;
            _size = buffer.Size;

            ReleaseImpl();;
        }

        public BufferView GetBufferView(CommandBufferScoped cbs)
        {
            if (_bufferView == null)
            {
                _bufferView = _gd.BufferManager.CreateView(_bufferHandle, VkFormat, _offset, _size);
            }

            return _bufferView?.Get(cbs, _offset, _size).Value ?? default;
        }

        public BufferView GetBufferView(CommandBufferScoped cbs, GAL.Format format)
        {
            var vkFormat = FormatTable.GetFormat(format);
            if (vkFormat == VkFormat)
            {
                return GetBufferView(cbs);
            }

            if (_selfManagedViews != null && _selfManagedViews.TryGetValue(format, out var bufferView))
            {
                return bufferView.Get(cbs, _offset, _size).Value;
            }

            bufferView = _gd.BufferManager.CreateView(_bufferHandle, vkFormat, _offset, _size);

            if (bufferView != null)
            {
                (_selfManagedViews ??= new Dictionary<GAL.Format, Auto<DisposableBufferView>>()).Add(format, bufferView);
            }

            return bufferView?.Get(cbs, _offset, _size).Value ?? default;
        }
    }
}
