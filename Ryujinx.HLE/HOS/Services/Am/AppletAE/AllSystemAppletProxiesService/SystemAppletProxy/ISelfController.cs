using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Kernel.Common;
using Ryujinx.HLE.HOS.Kernel.Threading;
using System;

namespace Ryujinx.HLE.HOS.Services.Am.AppletAE.AllSystemAppletProxiesService.SystemAppletProxy
{
    class ISelfController : IpcService
    {
        private readonly ulong _pid;

        private KEvent _libraryAppletLaunchableEvent;
        private int    _libraryAppletLaunchableEventHandle;

        private KEvent _accumulatedSuspendedTickChangedEvent;
        private int    _accumulatedSuspendedTickChangedEventHandle;

        private object _fatalSectionLock = new object();
        private int    _fatalSectionCount;

        // TODO: Set this when the game goes in suspension (go back to home menu ect), we currently don't support that so we can keep it set to 0.
        private ulong _accumulatedSuspendedTickValue = 0;

        // TODO: Determine where those fields are used.
        private bool _screenShotPermission               = false;
        private bool _operationModeChangedNotification   = false;
        private bool _performanceModeChangedNotification = false;
        private bool _restartMessageEnabled              = false;
        private bool _outOfFocusSuspendingEnabled        = false;
        private bool _handlesRequestToDisplay            = false;
        private bool _autoSleepDisabled                  = false;
        private bool _albumImageTakenNotificationEnabled = false;

        private uint _screenShotImageOrientation = 0;
        private uint _idleTimeDetectionExtension = 0;

        public ISelfController(ServiceCtx context, ulong pid)
        {
            _libraryAppletLaunchableEvent = new KEvent(context.Device.System.KernelContext);
            _pid = pid;
        }

        [CommandHipc(0)]
        // Exit()
        public ResultCode Exit(ServiceCtx context)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceAm);

            return ResultCode.Success;
        }

        [CommandHipc(1)]
        // LockExit()
        public ResultCode LockExit(ServiceCtx context)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceAm);

            return ResultCode.Success;
        }

        [CommandHipc(2)]
        // UnlockExit()
        public ResultCode UnlockExit(ServiceCtx context)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceAm);

            return ResultCode.Success;
        }

        [CommandHipc(3)] // 2.0.0+
        // EnterFatalSection()
        public ResultCode EnterFatalSection(ServiceCtx context)
        {
            lock (_fatalSectionLock)
            {
                _fatalSectionCount++;
            }

            return ResultCode.Success;
        }

        [CommandHipc(4)] // 2.0.0+
        // LeaveFatalSection()
        public ResultCode LeaveFatalSection(ServiceCtx context)
        {
            ResultCode result = ResultCode.Success;

            lock (_fatalSectionLock)
            {
                if (_fatalSectionCount != 0)
                {
                    _fatalSectionCount--;
                }
                else
                {
                    result = ResultCode.UnbalancedFatalSection;
                }
            }

            return result;
        }

        [CommandHipc(9)]
        // GetLibraryAppletLaunchableEvent() -> handle<copy>
        public ResultCode GetLibraryAppletLaunchableEvent(ServiceCtx context)
        {
            _libraryAppletLaunchableEvent.ReadableEvent.Signal();

            if (_libraryAppletLaunchableEventHandle == 0)
            {
                if (context.Process.HandleTable.GenerateHandle(_libraryAppletLaunchableEvent.ReadableEvent, out _libraryAppletLaunchableEventHandle) != KernelResult.Success)
                {
                    throw new InvalidOperationException("Out of handles!");
                }
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(_libraryAppletLaunchableEventHandle);

            Logger.Stub?.PrintStub(LogClass.ServiceAm);

            return ResultCode.Success;
        }

        [CommandHipc(10)]
        // SetScreenShotPermission(u32)
        public ResultCode SetScreenShotPermission(ServiceCtx context)
        {
            bool screenShotPermission = context.RequestData.ReadBoolean();

            Logger.Stub?.PrintStub(LogClass.ServiceAm, new { screenShotPermission });

            _screenShotPermission = screenShotPermission;

            return ResultCode.Success;
        }

        [CommandHipc(11)]
        // SetOperationModeChangedNotification(b8)
        public ResultCode SetOperationModeChangedNotification(ServiceCtx context)
        {
            bool operationModeChangedNotification = context.RequestData.ReadBoolean();

            Logger.Stub?.PrintStub(LogClass.ServiceAm, new { operationModeChangedNotification });

            _operationModeChangedNotification = operationModeChangedNotification;

            return ResultCode.Success;
        }

        [CommandHipc(12)]
        // SetPerformanceModeChangedNotification(b8)
        public ResultCode SetPerformanceModeChangedNotification(ServiceCtx context)
        {
            bool performanceModeChangedNotification = context.RequestData.ReadBoolean();

            Logger.Stub?.PrintStub(LogClass.ServiceAm, new { performanceModeChangedNotification });

            _performanceModeChangedNotification = performanceModeChangedNotification;

            return ResultCode.Success;
        }

        [CommandHipc(13)]
        // SetFocusHandlingMode(b8, b8, b8)
        public ResultCode SetFocusHandlingMode(ServiceCtx context)
        {
            bool unknownFlag1 = context.RequestData.ReadBoolean();
            bool unknownFlag2 = context.RequestData.ReadBoolean();
            bool unknownFlag3 = context.RequestData.ReadBoolean();

            Logger.Stub?.PrintStub(LogClass.ServiceAm, new { unknownFlag1, unknownFlag2, unknownFlag3 });

            return ResultCode.Success;
        }

        [CommandHipc(14)]
        // SetRestartMessageEnabled(b8)
        public ResultCode SetRestartMessageEnabled(ServiceCtx context)
        {
            bool restartMessageEnabled = context.RequestData.ReadBoolean();

            Logger.Stub?.PrintStub(LogClass.ServiceAm, new { restartMessageEnabled });

            _restartMessageEnabled = restartMessageEnabled;

            return ResultCode.Success;
        }

        [CommandHipc(16)] // 2.0.0+
        // SetOutOfFocusSuspendingEnabled(b8)
        public ResultCode SetOutOfFocusSuspendingEnabled(ServiceCtx context)
        {
            bool outOfFocusSuspendingEnabled = context.RequestData.ReadBoolean();

            Logger.Stub?.PrintStub(LogClass.ServiceAm, new { outOfFocusSuspendingEnabled });

            _outOfFocusSuspendingEnabled = outOfFocusSuspendingEnabled;

            return ResultCode.Success;
        }

        [CommandHipc(19)] // 3.0.0+
        // SetScreenShotImageOrientation(u32)
        public ResultCode SetScreenShotImageOrientation(ServiceCtx context)
        {
            uint screenShotImageOrientation = context.RequestData.ReadUInt32();

            Logger.Stub?.PrintStub(LogClass.ServiceAm, new { screenShotImageOrientation });

            _screenShotImageOrientation = screenShotImageOrientation;

            return ResultCode.Success;
        }

        [CommandHipc(40)]
        // CreateManagedDisplayLayer() -> u64
        public ResultCode CreateManagedDisplayLayer(ServiceCtx context)
        {
            context.Device.System.SurfaceFlinger.CreateLayer(_pid, out long layerId);
            context.Device.System.SurfaceFlinger.SetRenderLayer(layerId);

            context.ResponseData.Write(layerId);

            return ResultCode.Success;
        }

        [CommandHipc(41)] // 4.0.0+
        // IsSystemBufferSharingEnabled()
        public ResultCode IsSystemBufferSharingEnabled(ServiceCtx context)
        {
            // NOTE: Service checks a private field and return an error if the SystemBufferSharing is disabled.

            return ResultCode.NotImplemented;
        }

        [CommandHipc(44)] // 10.0.0+
        // CreateManagedDisplaySeparableLayer() -> (u64, u64)
        public ResultCode CreateManagedDisplaySeparableLayer(ServiceCtx context)
        {
            context.Device.System.SurfaceFlinger.CreateLayer(_pid, out long displayLayerId);
            context.Device.System.SurfaceFlinger.CreateLayer(_pid, out long recordingLayerId);
            context.Device.System.SurfaceFlinger.SetRenderLayer(displayLayerId);

            context.ResponseData.Write(displayLayerId);
            context.ResponseData.Write(recordingLayerId);

            return ResultCode.Success;
        }

        [CommandHipc(50)]
        // SetHandlesRequestToDisplay(b8)
        public ResultCode SetHandlesRequestToDisplay(ServiceCtx context)
        {
            bool handlesRequestToDisplay = context.RequestData.ReadBoolean();

            Logger.Stub?.PrintStub(LogClass.ServiceAm, new { handlesRequestToDisplay });

            _handlesRequestToDisplay = handlesRequestToDisplay;

            return ResultCode.Success;
        }

        [CommandHipc(62)]
        // SetIdleTimeDetectionExtension(u32)
        public ResultCode SetIdleTimeDetectionExtension(ServiceCtx context)
        {
            uint idleTimeDetectionExtension = context.RequestData.ReadUInt32();

            Logger.Stub?.PrintStub(LogClass.ServiceAm, new { idleTimeDetectionExtension });

            _idleTimeDetectionExtension = idleTimeDetectionExtension;

            return ResultCode.Success;
        }

        [CommandHipc(63)]
        // GetIdleTimeDetectionExtension() -> u32
        public ResultCode GetIdleTimeDetectionExtension(ServiceCtx context)
        {
            context.ResponseData.Write(_idleTimeDetectionExtension);

            Logger.Stub?.PrintStub(LogClass.ServiceAm, new { _idleTimeDetectionExtension });

            return ResultCode.Success;
        }

        [CommandHipc(65)]
        // ReportUserIsActive()
        public ResultCode ReportUserIsActive(ServiceCtx context)
        {
            // TODO: Call idle:sys ReportUserIsActive when implemented.

            Logger.Stub?.PrintStub(LogClass.ServiceAm);

            return ResultCode.Success;
        }

        [CommandHipc(68)]
        // SetAutoSleepDisabled(u8)
        public ResultCode SetAutoSleepDisabled(ServiceCtx context)
        {
            bool autoSleepDisabled = context.RequestData.ReadBoolean();

            _autoSleepDisabled = autoSleepDisabled;

            return ResultCode.Success;
        }

        [CommandHipc(69)]
        // IsAutoSleepDisabled() -> u8
        public ResultCode IsAutoSleepDisabled(ServiceCtx context)
        {
            context.ResponseData.Write(_autoSleepDisabled);

            return ResultCode.Success;
        }

        [CommandHipc(90)] // 6.0.0+
        // GetAccumulatedSuspendedTickValue() -> u64
        public ResultCode GetAccumulatedSuspendedTickValue(ServiceCtx context)
        {
            context.ResponseData.Write(_accumulatedSuspendedTickValue);

            return ResultCode.Success;
        }

        [CommandHipc(91)] // 6.0.0+
        // GetAccumulatedSuspendedTickChangedEvent() -> handle<copy>
        public ResultCode GetAccumulatedSuspendedTickChangedEvent(ServiceCtx context)
        {
            if (_accumulatedSuspendedTickChangedEventHandle == 0)
            {
                _accumulatedSuspendedTickChangedEvent = new KEvent(context.Device.System.KernelContext);

                _accumulatedSuspendedTickChangedEvent.ReadableEvent.Signal();

                if (context.Process.HandleTable.GenerateHandle(_accumulatedSuspendedTickChangedEvent.ReadableEvent, out _accumulatedSuspendedTickChangedEventHandle) != KernelResult.Success)
                {
                    throw new InvalidOperationException("Out of handles!");
                }
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(_accumulatedSuspendedTickChangedEventHandle);

            return ResultCode.Success;
        }

        [CommandHipc(100)] // 7.0.0+
        // SetAlbumImageTakenNotificationEnabled(u8)
        public ResultCode SetAlbumImageTakenNotificationEnabled(ServiceCtx context)
        {
            bool albumImageTakenNotificationEnabled = context.RequestData.ReadBoolean();

            _albumImageTakenNotificationEnabled = albumImageTakenNotificationEnabled;

            return ResultCode.Success;
        }
    }
}