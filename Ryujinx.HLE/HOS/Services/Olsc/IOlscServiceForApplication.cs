﻿using Ryujinx.Common;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Account.Acc;
using System.Collections.Generic;

namespace Ryujinx.HLE.HOS.Services.Olsc
{
    [Service("olsc:u")] // 10.0.0+
    class IOlscServiceForApplication : IpcService
    {
        private bool                     _initialized;
        private Dictionary<UserId, bool> _saveDataBackupSettingDatabase;

        public IOlscServiceForApplication(ServiceCtx context) { }

        [CommandHipc(0)]
        // Initialize(pid)
        public ResultCode Initialize(ServiceCtx context)
        {
            // NOTE: Service call arp:r GetApplicationInstanceUnregistrationNotifier with the pid and initialize some internal struct.
            //       Since we will not support online savedata backup, it's fine to stub it for now.

            _saveDataBackupSettingDatabase = new Dictionary<UserId, bool>();

            _initialized = true;

            Logger.Stub?.PrintStub(LogClass.ServiceOlsc);

            return ResultCode.Success;
        }

        [CommandHipc(13)]
        // GetSaveDataBackupSetting(nn::account::Uid) -> u8
        public ResultCode GetSaveDataBackupSetting(ServiceCtx context)
        {
            UserId userId = context.RequestData.ReadStruct<UserId>();

            if (!_initialized)
            {
                return ResultCode.NotInitialized;
            }

            if (userId.IsNull)
            {
                return ResultCode.NullArgument;
            }

            if (_saveDataBackupSettingDatabase.TryGetValue(userId, out bool enabled) && enabled)
            {
                context.ResponseData.Write((byte)1); // TODO: Determine value.
            }
            else
            {
                context.ResponseData.Write((byte)2); // TODO: Determine value.
            }

            // NOTE: Since we will not support online savedata backup, it's fine to stub it for now.

            Logger.Stub?.PrintStub(LogClass.ServiceOlsc, new { userId });

            return ResultCode.Success;
        }

        [CommandHipc(14)]
        // SetSaveDataBackupSettingEnabled(nn::account::Uid, bool)
        public ResultCode SetSaveDataBackupSettingEnabled(ServiceCtx context)
        {
            bool   saveDataBackupSettingEnabled = context.RequestData.ReadUInt64() != 0;
            UserId userId                       = context.RequestData.ReadStruct<UserId>();

            if (!_initialized)
            {
                return ResultCode.NotInitialized;
            }

            if (userId.IsNull)
            {
                return ResultCode.NullArgument;
            }

            _saveDataBackupSettingDatabase[userId] = saveDataBackupSettingEnabled;

            // NOTE: Since we will not support online savedata backup, it's fine to stub it for now.

            Logger.Stub?.PrintStub(LogClass.ServiceOlsc, new { userId, saveDataBackupSettingEnabled });

            return ResultCode.Success;
        }
    }
}