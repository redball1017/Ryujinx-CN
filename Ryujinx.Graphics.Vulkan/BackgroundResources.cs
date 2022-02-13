﻿using System.Threading;
using System.Collections.Generic;
using System;
using Silk.NET.Vulkan;

namespace Ryujinx.Graphics.Vulkan
{
    class BackgroundResource : IDisposable
    {
        private VulkanGraphicsDevice _gd;
        private Device _device;

        private CommandBufferPool _pool;
        private PersistentFlushBuffer _flushBuffer;

        public BackgroundResource(VulkanGraphicsDevice gd, Device device)
        {
            _gd = gd;
            _device = device;
        }

        public CommandBufferPool GetPool()
        {
            if (_pool == null)
            {
                bool useBackground = _gd.BackgroundQueue.Handle != 0 && _gd.Vendor != Vendor.Amd;
                Queue queue = useBackground ? _gd.BackgroundQueue : _gd.Queue;
                object queueLock = useBackground ? _gd.BackgroundQueueLock : _gd.QueueLock;

                lock (queueLock)
                {
                    _pool = new CommandBufferPool(_gd.Api, _device, queue, queueLock, _gd.QueueFamilyIndex, isLight: true);
                }
            }

            return _pool;
        }

        public PersistentFlushBuffer GetFlushBuffer()
        {
            if (_flushBuffer == null)
            {
                _flushBuffer = new PersistentFlushBuffer(_gd);
            }

            return _flushBuffer;
        }

        public void Dispose()
        {
            _pool?.Dispose();
            _flushBuffer?.Dispose();
        }
    }

    class BackgroundResources : IDisposable
    {
        private VulkanGraphicsDevice _gd;
        private Device _device;

        private Dictionary<Thread, BackgroundResource> _resources;

        public BackgroundResources(VulkanGraphicsDevice gd, Device device)
        {
            _gd = gd;
            _device = device;

            _resources = new Dictionary<Thread, BackgroundResource>();
        }

        private void Cleanup()
        {
            foreach (KeyValuePair<Thread, BackgroundResource> tuple in _resources)
            {
                if (!tuple.Key.IsAlive)
                {
                    tuple.Value.Dispose();
                    _resources.Remove(tuple.Key);
                }
            }
        }

        public BackgroundResource Get()
        {
            Thread thread = Thread.CurrentThread;

            lock (_resources)
            {
                BackgroundResource resource;
                if (!_resources.TryGetValue(thread, out resource))
                {
                    Cleanup();

                    resource = new BackgroundResource(_gd, _device);

                    _resources[thread] = resource;
                }

                return resource;
            }
        }

        public void Dispose()
        {
            lock (_resources)
            {
                foreach (var resource in _resources.Values)
                {
                    resource.Dispose();
                }
            }
        }
    }
}
