﻿using Ryujinx.Common;
using Ryujinx.Cpu;
using Ryujinx.HLE.Exceptions;
using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Kernel.Common;
using Ryujinx.HLE.HOS.Services.Time.Clock;
using Ryujinx.HLE.Utilities;
using System;
using System.IO;

namespace Ryujinx.HLE.HOS.Services.Time
{
    [Service("time:m")] // 9.0.0+
    class ITimeServiceManager : IpcService
    {
        private TimeManager _timeManager;
        private int         _automaticCorrectionEvent;

        public ITimeServiceManager(ServiceCtx context)
        {
            _timeManager              = TimeManager.Instance;
            _automaticCorrectionEvent = 0;
        }

        [CommandHipc(0)]
        // GetUserStaticService() -> object<nn::timesrv::detail::service::IStaticService>
        public ResultCode GetUserStaticService(ServiceCtx context)
        {
            MakeObject(context, new IStaticServiceForPsc(_timeManager, TimePermissions.User));

            return ResultCode.Success;
        }

        [CommandHipc(5)]
        // GetAdminStaticService() -> object<nn::timesrv::detail::service::IStaticService>
        public ResultCode GetAdminStaticService(ServiceCtx context)
        {
            MakeObject(context, new IStaticServiceForPsc(_timeManager, TimePermissions.Admin));

            return ResultCode.Success;
        }

        [CommandHipc(6)]
        // GetRepairStaticService() -> object<nn::timesrv::detail::service::IStaticService>
        public ResultCode GetRepairStaticService(ServiceCtx context)
        {
            MakeObject(context, new IStaticServiceForPsc(_timeManager, TimePermissions.Repair));

            return ResultCode.Success;
        }

        [CommandHipc(9)]
        // GetManufactureStaticService() -> object<nn::timesrv::detail::service::IStaticService>
        public ResultCode GetManufactureStaticService(ServiceCtx context)
        {
            MakeObject(context, new IStaticServiceForPsc(_timeManager, TimePermissions.Manufacture));

            return ResultCode.Success;
        }

        [CommandHipc(10)]
        // SetupStandardSteadyClock(nn::util::Uuid clock_source_id, nn::TimeSpanType setup_value,  nn::TimeSpanType internal_offset,  nn::TimeSpanType test_offset, bool is_rtc_reset_detected)
        public ResultCode SetupStandardSteadyClock(ServiceCtx context)
        {
            UInt128      clockSourceId      = context.RequestData.ReadStruct<UInt128>();
            TimeSpanType setupValue         = context.RequestData.ReadStruct<TimeSpanType>();
            TimeSpanType internalOffset     = context.RequestData.ReadStruct<TimeSpanType>();
            TimeSpanType testOffset         = context.RequestData.ReadStruct<TimeSpanType>();
            bool         isRtcResetDetected = context.RequestData.ReadBoolean();

            ITickSource tickSource = context.Device.System.TickSource;

            _timeManager.SetupStandardSteadyClock(tickSource, clockSourceId, setupValue, internalOffset, testOffset, isRtcResetDetected);

            return ResultCode.Success;
        }

        [CommandHipc(11)]
        // SetupStandardLocalSystemClock(nn::time::SystemClockContext context, nn::time::PosixTime posix_time)
        public ResultCode SetupStandardLocalSystemClock(ServiceCtx context)
        {
            SystemClockContext clockContext = context.RequestData.ReadStruct<SystemClockContext>();
            long               posixTime    = context.RequestData.ReadInt64();

            ITickSource tickSource = context.Device.System.TickSource;

            _timeManager.SetupStandardLocalSystemClock(tickSource, clockContext, posixTime);

            return ResultCode.Success;
        }

        [CommandHipc(12)]
        // SetupStandardNetworkSystemClock(nn::time::SystemClockContext context, nn::TimeSpanType sufficient_accuracy)
        public ResultCode SetupStandardNetworkSystemClock(ServiceCtx context)
        {
            SystemClockContext clockContext       = context.RequestData.ReadStruct<SystemClockContext>();
            TimeSpanType       sufficientAccuracy = context.RequestData.ReadStruct<TimeSpanType>();

            _timeManager.SetupStandardNetworkSystemClock(clockContext, sufficientAccuracy);

            return ResultCode.Success;
        }

        [CommandHipc(13)]
        // SetupStandardUserSystemClock(bool automatic_correction_enabled, nn::time::SteadyClockTimePoint steady_clock_timepoint)
        public ResultCode SetupStandardUserSystemClock(ServiceCtx context)
        {
            bool isAutomaticCorrectionEnabled = context.RequestData.ReadBoolean();

            context.RequestData.BaseStream.Position += 7;

            SteadyClockTimePoint steadyClockTimePoint = context.RequestData.ReadStruct<SteadyClockTimePoint>();

            ITickSource tickSource = context.Device.System.TickSource;

            _timeManager.SetupStandardUserSystemClock(tickSource, isAutomaticCorrectionEnabled, steadyClockTimePoint);

            return ResultCode.Success;
        }

        [CommandHipc(14)]
        // SetupTimeZoneManager(nn::time::LocationName location_name, nn::time::SteadyClockTimePoint timezone_update_timepoint, u32 total_location_name_count, nn::time::TimeZoneRuleVersion timezone_rule_version, buffer<nn::time::TimeZoneBinary, 0x21> timezone_binary)
        public ResultCode SetupTimeZoneManager(ServiceCtx context)
        {
            string               locationName            = StringUtils.ReadInlinedAsciiString(context.RequestData, 0x24);
            SteadyClockTimePoint timeZoneUpdateTimePoint = context.RequestData.ReadStruct<SteadyClockTimePoint>();
            uint                 totalLocationNameCount  = context.RequestData.ReadUInt32();
            UInt128              timeZoneRuleVersion     = context.RequestData.ReadStruct<UInt128>();

            (ulong bufferPosition, ulong bufferSize) = context.Request.GetBufferType0x21();

            byte[] temp = new byte[bufferSize];

            context.Memory.Read(bufferPosition, temp);

            using (MemoryStream timeZoneBinaryStream = new MemoryStream(temp))
            {
                _timeManager.SetupTimeZoneManager(locationName, timeZoneUpdateTimePoint, totalLocationNameCount, timeZoneRuleVersion, timeZoneBinaryStream);
            }

            return ResultCode.Success;
        }

        [CommandHipc(15)]
        // SetupEphemeralNetworkSystemClock()
        public ResultCode SetupEphemeralNetworkSystemClock(ServiceCtx context)
        {
            _timeManager.SetupEphemeralNetworkSystemClock();

            return ResultCode.Success;
        }

        [CommandHipc(50)]
        // Unknown50() -> handle<copy>
        public ResultCode Unknown50(ServiceCtx context)
        {
            // TODO: figure out the usage of this event
            throw new ServiceNotImplementedException(this, context);
        }

        [CommandHipc(51)]
        // Unknown51() -> handle<copy>
        public ResultCode Unknown51(ServiceCtx context)
        {
            // TODO: figure out the usage of this event
            throw new ServiceNotImplementedException(this, context);
        }

        [CommandHipc(52)]
        // Unknown52() -> handle<copy>
        public ResultCode Unknown52(ServiceCtx context)
        {
            // TODO: figure out the usage of this event
            throw new ServiceNotImplementedException(this, context);
        }

        [CommandHipc(60)]
        // GetStandardUserSystemClockAutomaticCorrectionEvent() -> handle<copy>
        public ResultCode GetStandardUserSystemClockAutomaticCorrectionEvent(ServiceCtx context)
        {
            if (_automaticCorrectionEvent == 0)
            {
                if (context.Process.HandleTable.GenerateHandle(_timeManager.StandardUserSystemClock.GetAutomaticCorrectionReadableEvent(), out _automaticCorrectionEvent) != KernelResult.Success)
                {
                    throw new InvalidOperationException("Out of handles!");
                }
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(_automaticCorrectionEvent);

            return ResultCode.Success;
        }

        [CommandHipc(100)]
        // SetStandardSteadyClockRtcOffset(nn::TimeSpanType rtc_offset)
        public ResultCode SetStandardSteadyClockRtcOffset(ServiceCtx context)
        {
            TimeSpanType rtcOffset = context.RequestData.ReadStruct<TimeSpanType>();

            ITickSource tickSource = context.Device.System.TickSource;

            _timeManager.SetStandardSteadyClockRtcOffset(tickSource, rtcOffset);

            return ResultCode.Success;
        }

        [CommandHipc(200)]
        // GetAlarmRegistrationEvent() -> handle<copy>
        public ResultCode GetAlarmRegistrationEvent(ServiceCtx context)
        {
            // TODO
            throw new ServiceNotImplementedException(this, context);
        }

        [CommandHipc(201)]
        // UpdateSteadyAlarms()
        public ResultCode UpdateSteadyAlarms(ServiceCtx context)
        {
            // TODO
            throw new ServiceNotImplementedException(this, context);
        }

        [CommandHipc(202)]
        // TryGetNextSteadyClockAlarmSnapshot() -> (bool, nn::time::SteadyClockAlarmSnapshot)
        public ResultCode TryGetNextSteadyClockAlarmSnapshot(ServiceCtx context)
        {
            // TODO
            throw new ServiceNotImplementedException(this, context);
        }
    }
}
