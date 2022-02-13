using Ryujinx.Common.Logging;
using Silk.NET.Vulkan;
using System.Collections.Generic;
using System.Linq;

namespace Ryujinx.Graphics.Vulkan
{
    class SyncManager
    {
        private class SyncHandle
        {
            public ulong ID;
            public MultiFenceHolder Waitable;
        }

        private ulong _firstHandle = 0;

        private readonly VulkanGraphicsDevice _gd;
        private readonly Device _device;
        private List<SyncHandle> _handles;

        public SyncManager(VulkanGraphicsDevice gd, Device device)
        {
            _gd = gd;
            _device = device;
            _handles = new List<SyncHandle>();
        }

        public void Create(ulong id)
        {
            MultiFenceHolder waitable = new MultiFenceHolder();

            _gd.FlushAllCommands();
            _gd.CommandBufferPool.AddWaitable(waitable);

            SyncHandle handle = new SyncHandle
            {
                ID = id,
                Waitable = waitable
            };

            lock (_handles)
            {
                _handles.Add(handle);
            }
        }

        public void Wait(ulong id)
        {
            SyncHandle result = null;

            lock (_handles)
            {
                if ((long)(_firstHandle - id) > 0)
                {
                    return; // The handle has already been signalled or deleted.
                }

                foreach (SyncHandle handle in _handles)
                {
                    if (handle.ID == id)
                    {
                        result = handle;
                        break;
                    }
                }
            }

            if (result != null)
            {
                lock (result)
                {
                    if (result.Waitable == null)
                    {
                        return;
                    }

                    bool signaled = result.Waitable.WaitForFences(_gd.Api, _device, 1000000000);
                    if (!signaled)
                    {
                        Logger.Error?.PrintMsg(LogClass.Gpu, $"GL Sync Object {result.ID} failed to signal within 1000ms. Continuing...");
                    }
                }
            }
        }

        public void Cleanup()
        {
            // Iterate through handles and remove any that have already been signalled.

            while (true)
            {
                SyncHandle first = null;
                lock (_handles)
                {
                    first = _handles.FirstOrDefault();
                }

                if (first == null) break;

                bool signaled = first.Waitable.WaitForFences(_gd.Api, _device, 0);
                if (signaled)
                {
                    // Delete the sync object.
                    lock (_handles)
                    {
                        lock (first)
                        {
                            _firstHandle = first.ID + 1;
                            _handles.RemoveAt(0);
                            first.Waitable = null;
                        }
                    }
                } else
                {
                    // This sync handle and any following have not been reached yet.
                    break;
                }
            }
        }
    }
}
