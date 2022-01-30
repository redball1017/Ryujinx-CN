using Ryujinx.Common.Logging;
using Ryujinx.HLE.Exceptions;
using Ryujinx.HLE.HOS.Ipc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;

namespace Ryujinx.HLE.HOS.Services
{
    abstract class IpcService
    {
        public IReadOnlyDictionary<int, MethodInfo> HipcCommands { get; }
        public IReadOnlyDictionary<int, MethodInfo> TipcCommands { get; }

        public ServerBase Server { get; private set; }

        private IpcService _parent;
        private IdDictionary _domainObjects;
        private int _selfId;
        private bool _isDomain;

        public IpcService(ServerBase server = null)
        {
            HipcCommands = Assembly.GetExecutingAssembly().GetTypes()
                .Where(type => type == GetType())
                .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public))
                .SelectMany(methodInfo => methodInfo.GetCustomAttributes(typeof(CommandHipcAttribute))
                .Select(command => (((CommandHipcAttribute)command).Id, methodInfo)))
                .ToDictionary(command => command.Id, command => command.methodInfo);

            TipcCommands = Assembly.GetExecutingAssembly().GetTypes()
                .Where(type => type == GetType())
                .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public))
                .SelectMany(methodInfo => methodInfo.GetCustomAttributes(typeof(CommandTipcAttribute))
                .Select(command => (((CommandTipcAttribute)command).Id, methodInfo)))
                .ToDictionary(command => command.Id, command => command.methodInfo);

            Server = server;

            _parent = this;
            _domainObjects = new IdDictionary();
            _selfId = -1;
        }

        public int ConvertToDomain()
        {
            if (_selfId == -1)
            {
                _selfId = _domainObjects.Add(this);
            }

            _isDomain = true;

            return _selfId;
        }

        public void ConvertToSession()
        {
            _isDomain = false;
        }

        public void CallHipcMethod(ServiceCtx context)
        {
            IpcService service = this;

            if (_isDomain)
            {
                int domainWord0 = context.RequestData.ReadInt32();
                int domainObjId = context.RequestData.ReadInt32();

                int domainCmd = (domainWord0 >> 0) & 0xff;
                int inputObjCount = (domainWord0 >> 8) & 0xff;
                int dataPayloadSize = (domainWord0 >> 16) & 0xffff;

                context.RequestData.BaseStream.Seek(0x10 + dataPayloadSize, SeekOrigin.Begin);

                for (int index = 0; index < inputObjCount; index++)
                {
                    context.Request.ObjectIds.Add(context.RequestData.ReadInt32());
                }

                context.RequestData.BaseStream.Seek(0x10, SeekOrigin.Begin);

                if (domainCmd == 1)
                {
                    service = GetObject(domainObjId);

                    context.ResponseData.Write(0L);
                    context.ResponseData.Write(0L);
                }
                else if (domainCmd == 2)
                {
                    Delete(domainObjId);

                    context.ResponseData.Write(0L);

                    return;
                }
                else
                {
                    throw new NotImplementedException($"Domain command: {domainCmd}");
                }
            }

            long sfciMagic = context.RequestData.ReadInt64();
            int commandId = (int)context.RequestData.ReadInt64();

            bool serviceExists = service.HipcCommands.TryGetValue(commandId, out MethodInfo processRequest);

            if (context.Device.Configuration.IgnoreMissingServices || serviceExists)
            {
                ResultCode result = ResultCode.Success;

                context.ResponseData.BaseStream.Seek(_isDomain ? 0x20 : 0x10, SeekOrigin.Begin);

                if (serviceExists)
                {
                    Logger.Debug?.Print(LogClass.KernelIpc, $"{service.GetType().Name}: {processRequest.Name}");

                    result = (ResultCode)processRequest.Invoke(service, new object[] { context });
                }
                else
                {
                    string serviceName;

                    DummyService dummyService = service as DummyService;

                    serviceName = (dummyService == null) ? service.GetType().FullName : dummyService.ServiceName;

                    Logger.Warning?.Print(LogClass.KernelIpc, $"Missing service {serviceName}: {commandId} ignored");
                }

                if (_isDomain)
                {
                    foreach (int id in context.Response.ObjectIds)
                    {
                        context.ResponseData.Write(id);
                    }

                    context.ResponseData.BaseStream.Seek(0, SeekOrigin.Begin);

                    context.ResponseData.Write(context.Response.ObjectIds.Count);
                }

                context.ResponseData.BaseStream.Seek(_isDomain ? 0x10 : 0, SeekOrigin.Begin);

                context.ResponseData.Write(IpcMagic.Sfco);
                context.ResponseData.Write((long)result);
            }
            else
            {
                string dbgMessage = $"{service.GetType().FullName}: {commandId}";

                throw new ServiceNotImplementedException(service, context, dbgMessage);
            }
        }

        public void CallTipcMethod(ServiceCtx context)
        {
            int commandId = (int)context.Request.Type - 0x10;

            bool serviceExists = TipcCommands.TryGetValue(commandId, out MethodInfo processRequest);

            if (context.Device.Configuration.IgnoreMissingServices || serviceExists)
            {
                ResultCode result = ResultCode.Success;

                context.ResponseData.BaseStream.Seek(0x4, SeekOrigin.Begin);

                if (serviceExists)
                {
                    Logger.Debug?.Print(LogClass.KernelIpc, $"{GetType().Name}: {processRequest.Name}");

                    result = (ResultCode)processRequest.Invoke(this, new object[] { context });
                }
                else
                {
                    string serviceName;

                    DummyService dummyService = this as DummyService;

                    serviceName = (dummyService == null) ? GetType().FullName : dummyService.ServiceName;

                    Logger.Warning?.Print(LogClass.KernelIpc, $"Missing service {serviceName}: {commandId} ignored");
                }

                context.ResponseData.BaseStream.Seek(0, SeekOrigin.Begin);

                context.ResponseData.Write((uint)result);
            }
            else
            {
                string dbgMessage = $"{GetType().FullName}: {commandId}";

                throw new ServiceNotImplementedException(this, context, dbgMessage);
            }
        }

        protected void MakeObject(ServiceCtx context, IpcService obj)
        {
            obj.TrySetServer(_parent.Server);

            if (_parent._isDomain)
            {
                obj._parent = _parent;

                context.Response.ObjectIds.Add(_parent.Add(obj));
            }
            else
            {
                context.Device.System.KernelContext.Syscall.CreateSession(out int serverSessionHandle, out int clientSessionHandle, false, 0);

                obj.Server.AddSessionObj(serverSessionHandle, obj);

                context.Response.HandleDesc = IpcHandleDesc.MakeMove(clientSessionHandle);
            }
        }

        protected T GetObject<T>(ServiceCtx context, int index) where T : IpcService
        {
            int objId = context.Request.ObjectIds[index];

            IpcService obj = _parent.GetObject(objId);

            return obj is T t ? t : null;
        }

        public bool TrySetServer(ServerBase newServer)
        {
            if (Server == null)
            {
                Server = newServer;

                return true;
            }

            return false;
        }

        private int Add(IpcService obj)
        {
            return _domainObjects.Add(obj);
        }

        private bool Delete(int id)
        {
            object obj = _domainObjects.Delete(id);

            if (obj is IDisposable disposableObj)
            {
                disposableObj.Dispose();
            }

            return obj != null;
        }

        private IpcService GetObject(int id)
        {
            return _domainObjects.GetData<IpcService>(id);
        }

        public void SetParent(IpcService parent)
        {
            _parent = parent._parent;
        }

        public virtual void DestroyAtExit()
        {
            foreach (object domainObject in _domainObjects.Values)
            {
                if (domainObject != this && domainObject is IDisposable disposableObj)
                {
                    disposableObj.Dispose();
                }
            }

            _domainObjects.Clear();
        }
    }
}
