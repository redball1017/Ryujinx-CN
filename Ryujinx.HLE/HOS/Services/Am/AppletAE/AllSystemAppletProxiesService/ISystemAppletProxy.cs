using Ryujinx.HLE.HOS.Services.Am.AppletAE.AllSystemAppletProxiesService.SystemAppletProxy;

namespace Ryujinx.HLE.HOS.Services.Am.AppletAE.AllSystemAppletProxiesService
{
    class ISystemAppletProxy : IpcService
    {
        private readonly long _pid;

        public ISystemAppletProxy(long pid)
        {
            _pid = pid;
        }

        [CommandHipc(0)]
        // GetCommonStateGetter() -> object<nn::am::service::ICommonStateGetter>
        public ResultCode GetCommonStateGetter(ServiceCtx context)
        {
            MakeObject(context, new ICommonStateGetter(context));

            return ResultCode.Success;
        }

        [CommandHipc(1)]
        // GetSelfController() -> object<nn::am::service::ISelfController>
        public ResultCode GetSelfController(ServiceCtx context)
        {
            MakeObject(context, new ISelfController(context, _pid));

            return ResultCode.Success;
        }

        [CommandHipc(2)]
        // GetWindowController() -> object<nn::am::service::IWindowController>
        public ResultCode GetWindowController(ServiceCtx context)
        {
            MakeObject(context, new IWindowController(_pid));

            return ResultCode.Success;
        }

        [CommandHipc(3)]
        // GetAudioController() -> object<nn::am::service::IAudioController>
        public ResultCode GetAudioController(ServiceCtx context)
        {
            MakeObject(context, new IAudioController());

            return ResultCode.Success;
        }

        [CommandHipc(4)]
        // GetDisplayController() -> object<nn::am::service::IDisplayController>
        public ResultCode GetDisplayController(ServiceCtx context)
        {
            MakeObject(context, new IDisplayController(context));

            return ResultCode.Success;
        }

        [CommandHipc(11)]
        // GetLibraryAppletCreator() -> object<nn::am::service::ILibraryAppletCreator>
        public ResultCode GetLibraryAppletCreator(ServiceCtx context)
        {
            MakeObject(context, new ILibraryAppletCreator());

            return ResultCode.Success;
        }

        [CommandHipc(20)]
        // GetHomeMenuFunctions() -> object<nn::am::service::IHomeMenuFunctions>
        public ResultCode GetHomeMenuFunctions(ServiceCtx context)
        {
            MakeObject(context, new IHomeMenuFunctions(context.Device.System));

            return ResultCode.Success;
        }

        [CommandHipc(21)]
        // GetGlobalStateController() -> object<nn::am::service::IGlobalStateController>
        public ResultCode GetGlobalStateController(ServiceCtx context)
        {
            MakeObject(context, new IGlobalStateController());

            return ResultCode.Success;
        }

        [CommandHipc(22)]
        // GetApplicationCreator() -> object<nn::am::service::IApplicationCreator>
        public ResultCode GetApplicationCreator(ServiceCtx context)
        {
            MakeObject(context, new IApplicationCreator());

            return ResultCode.Success;
        }

        [CommandHipc(1000)]
        // GetDebugFunctions() -> object<nn::am::service::IDebugFunctions>
        public ResultCode GetDebugFunctions(ServiceCtx context)
        {
            MakeObject(context, new IDebugFunctions());

            return ResultCode.Success;
        }
    }
}