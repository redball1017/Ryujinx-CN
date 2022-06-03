using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Kernel;
using Ryujinx.HLE.HOS.Kernel.Common;
using Ryujinx.HLE.HOS.Kernel.Ipc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Ryujinx.HLE.HOS.Services.Sm
{
    class IUserInterface : IpcService
    {
        private static Dictionary<string, Type> _services;

        private readonly SmRegistry _registry;
        private readonly ServerBase _commonServer;

        private bool _isInitialized;

        public IUserInterface(KernelContext context, SmRegistry registry)
        {
            _commonServer = new ServerBase(context, "CommonServer");
            _registry = registry;
        }

        static IUserInterface()
        {
            _services = Assembly.GetExecutingAssembly().GetTypes()
                .SelectMany(type => type.GetCustomAttributes(typeof(ServiceAttribute), true)
                .Select(service => (((ServiceAttribute)service).Name, type)))
                .ToDictionary(service => service.Name, service => service.type);
        }

        [CommandHipc(0)]
        [CommandTipc(0)] // 12.0.0+
        // Initialize(pid, u64 reserved)
        public ResultCode Initialize(ServiceCtx context)
        {
            _isInitialized = true;

            return ResultCode.Success;
        }

        [CommandTipc(1)] // 12.0.0+
        // GetService(ServiceName name) -> handle<move, session>
        public ResultCode GetServiceTipc(ServiceCtx context)
        {
            context.Response.HandleDesc = IpcHandleDesc.MakeMove(0);

            return GetService(context);
        }

        [CommandHipc(1)]
        public ResultCode GetService(ServiceCtx context)
        {
            if (!_isInitialized)
            {
                return ResultCode.NotInitialized;
            }

            string name = ReadName(context);

            if (name == string.Empty)
            {
                return ResultCode.InvalidName;
            }

            KSession session = new KSession(context.Device.System.KernelContext);

            if (_registry.TryGetService(name, out KPort port))
            {
                KernelResult result = port.EnqueueIncomingSession(session.ServerSession);

                if (result != KernelResult.Success)
                {
                    throw new InvalidOperationException($"Session enqueue on port returned error \"{result}\".");
                }

                if (context.Process.HandleTable.GenerateHandle(session.ClientSession, out int handle) != KernelResult.Success)
                {
                    throw new InvalidOperationException("Out of handles!");
                }

                session.ClientSession.DecrementReferenceCount();

                context.Response.HandleDesc = IpcHandleDesc.MakeMove(handle);
            }
            else
            {
                if (_services.TryGetValue(name, out Type type))
                {
                    ServiceAttribute serviceAttribute = (ServiceAttribute)type.GetCustomAttributes(typeof(ServiceAttribute)).First(service => ((ServiceAttribute)service).Name == name);

                    IpcService service = serviceAttribute.Parameter != null
                        ? (IpcService)Activator.CreateInstance(type, context, serviceAttribute.Parameter)
                        : (IpcService)Activator.CreateInstance(type, context);

                    service.TrySetServer(_commonServer);
                    service.Server.AddSessionObj(session.ServerSession, service);
                }
                else
                {
                    if (context.Device.Configuration.IgnoreMissingServices)
                    {
                        Logger.Warning?.Print(LogClass.Service, $"Missing service {name} ignored");
                    }
                    else
                    {
                        throw new NotImplementedException(name);
                    }
                }

                if (context.Process.HandleTable.GenerateHandle(session.ClientSession, out int handle) != KernelResult.Success)
                {
                    throw new InvalidOperationException("Out of handles!");
                }

                session.ServerSession.DecrementReferenceCount();
                session.ClientSession.DecrementReferenceCount();

                context.Response.HandleDesc = IpcHandleDesc.MakeMove(handle);
            }

            return ResultCode.Success;
        }

        [CommandHipc(2)]
        // RegisterService(ServiceName name, u8 isLight, u32 maxHandles) -> handle<move, port>
        public ResultCode RegisterServiceHipc(ServiceCtx context)
        {
            if (!_isInitialized)
            {
                return ResultCode.NotInitialized;
            }

            long namePosition = context.RequestData.BaseStream.Position;

            string name = ReadName(context);

            context.RequestData.BaseStream.Seek(namePosition + 8, SeekOrigin.Begin);

            bool isLight = (context.RequestData.ReadInt32() & 1) != 0;

            int maxSessions = context.RequestData.ReadInt32();

            return RegisterService(context, name, isLight, maxSessions);
        }

        [CommandTipc(2)] // 12.0.0+
        // RegisterService(ServiceName name, u32 maxHandles, u8 isLight) -> handle<move, port>
        public ResultCode RegisterServiceTipc(ServiceCtx context)
        {
            if (!_isInitialized)
            {
                context.Response.HandleDesc = IpcHandleDesc.MakeMove(0);

                return ResultCode.NotInitialized;
            }

            long namePosition = context.RequestData.BaseStream.Position;

            string name = ReadName(context);

            context.RequestData.BaseStream.Seek(namePosition + 8, SeekOrigin.Begin);

            int maxSessions = context.RequestData.ReadInt32();

            bool isLight = (context.RequestData.ReadInt32() & 1) != 0;

            return RegisterService(context, name, isLight, maxSessions);
        }

        private ResultCode RegisterService(ServiceCtx context, string name, bool isLight, int maxSessions)
        {
            if (string.IsNullOrEmpty(name))
            {
                return ResultCode.InvalidName;
            }

            Logger.Info?.Print(LogClass.ServiceSm, $"Register \"{name}\".");

            KPort port = new KPort(context.Device.System.KernelContext, maxSessions, isLight, 0);

            if (!_registry.TryRegister(name, port))
            {
                return ResultCode.AlreadyRegistered;
            }

            if (context.Process.HandleTable.GenerateHandle(port.ServerPort, out int handle) != KernelResult.Success)
            {
                throw new InvalidOperationException("Out of handles!");
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeMove(handle);

            return ResultCode.Success;
        }

        [CommandHipc(3)]
        [CommandTipc(3)] // 12.0.0+
        // UnregisterService(ServiceName name)
        public ResultCode UnregisterService(ServiceCtx context)
        {
            if (!_isInitialized)
            {
                return ResultCode.NotInitialized;
            }

            long namePosition = context.RequestData.BaseStream.Position;

            string name = ReadName(context);

            context.RequestData.BaseStream.Seek(namePosition + 8, SeekOrigin.Begin);

            bool isLight = (context.RequestData.ReadInt32() & 1) != 0;

            int maxSessions = context.RequestData.ReadInt32();

            if (string.IsNullOrEmpty(name))
            {
                return ResultCode.InvalidName;
            }

            if (!_registry.Unregister(name))
            {
                return ResultCode.NotRegistered;
            }

            return ResultCode.Success;
        }

        private static string ReadName(ServiceCtx context)
        {
            string name = string.Empty;

            for (int index = 0; index < 8 &&
                context.RequestData.BaseStream.Position <
                context.RequestData.BaseStream.Length; index++)
            {
                byte chr = context.RequestData.ReadByte();

                if (chr >= 0x20 && chr < 0x7f)
                {
                    name += (char)chr;
                }
            }

            return name;
        }

        public override void DestroyAtExit()
        {
            _commonServer.Dispose();

            base.DestroyAtExit();
        }
    }
}