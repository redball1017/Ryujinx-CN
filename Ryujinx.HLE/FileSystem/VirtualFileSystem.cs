using LibHac;
using LibHac.Common;
using LibHac.Common.Keys;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.Fs.Shim;
using LibHac.FsSrv;
using LibHac.FsSystem;
using LibHac.Ncm;
using LibHac.Spl;
using LibHac.Tools.Es;
using LibHac.Tools.Fs;
using LibHac.Tools.FsSystem;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.FileSystem.Content;
using Ryujinx.HLE.HOS;
using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

using Path = System.IO.Path;
using RightsId = LibHac.Fs.RightsId;

namespace Ryujinx.HLE.FileSystem
{
    public class VirtualFileSystem : IDisposable
    {
        public const string NandPath = AppDataManager.DefaultNandDir;
        public const string SdCardPath = AppDataManager.DefaultSdcardDir;

        public static string SafeNandPath = Path.Combine(NandPath, "safe");
        public static string SystemNandPath = Path.Combine(NandPath, "system");
        public static string UserNandPath = Path.Combine(NandPath, "user");

        private static bool _isInitialized = false;

        public KeySet KeySet { get; private set; }
        public EmulatedGameCard GameCard { get; private set; }
        public EmulatedSdCard SdCard { get; private set; }

        public ModLoader ModLoader { get; private set; }

        private VirtualFileSystem()
        {
            ReloadKeySet();
            ModLoader = new ModLoader(); // Should only be created once
        }

        public Stream RomFs { get; private set; }

        public void LoadRomFs(string fileName)
        {
            RomFs = new FileStream(fileName, FileMode.Open, FileAccess.Read);
        }

        public void SetRomFs(Stream romfsStream)
        {
            RomFs?.Close();
            RomFs = romfsStream;
        }

        public string GetFullPath(string basePath, string fileName)
        {
            if (fileName.StartsWith("//"))
            {
                fileName = fileName.Substring(2);
            }
            else if (fileName.StartsWith('/'))
            {
                fileName = fileName.Substring(1);
            }
            else
            {
                return null;
            }

            string fullPath = Path.GetFullPath(Path.Combine(basePath, fileName));

            if (!fullPath.StartsWith(GetBasePath()))
            {
                return null;
            }

            return fullPath;
        }

        internal string GetBasePath() => AppDataManager.BaseDirPath;
        internal string GetSdCardPath() => MakeFullPath(SdCardPath);
        public string GetNandPath() => MakeFullPath(NandPath);

        public string GetFullPartitionPath(string partitionPath)
        {
            return MakeFullPath(partitionPath);
        }

        public string SwitchPathToSystemPath(string switchPath)
        {
            string[] parts = switchPath.Split(":");

            if (parts.Length != 2)
            {
                return null;
            }

            return GetFullPath(MakeFullPath(parts[0]), parts[1]);
        }

        public string SystemPathToSwitchPath(string systemPath)
        {
            string baseSystemPath = GetBasePath() + Path.DirectorySeparatorChar;

            if (systemPath.StartsWith(baseSystemPath))
            {
                string rawPath = systemPath.Replace(baseSystemPath, "");
                int firstSeparatorOffset = rawPath.IndexOf(Path.DirectorySeparatorChar);

                if (firstSeparatorOffset == -1)
                {
                    return $"{rawPath}:/";
                }

                string basePath = rawPath.Substring(0, firstSeparatorOffset);
                string fileName = rawPath.Substring(firstSeparatorOffset + 1);

                return $"{basePath}:/{fileName}";
            }
            return null;
        }

        private string MakeFullPath(string path, bool isDirectory = true)
        {
            // Handles Common Switch Content Paths
            switch (path)
            {
                case ContentPath.SdCard:
                case "@Sdcard":
                    path = SdCardPath;
                    break;
                case ContentPath.User:
                    path = UserNandPath;
                    break;
                case ContentPath.System:
                    path = SystemNandPath;
                    break;
                case ContentPath.SdCardContent:
                    path = Path.Combine(SdCardPath, "Nintendo", "Contents");
                    break;
                case ContentPath.UserContent:
                    path = Path.Combine(UserNandPath, "Contents");
                    break;
                case ContentPath.SystemContent:
                    path = Path.Combine(SystemNandPath, "Contents");
                    break;
            }

            string fullPath = Path.Combine(GetBasePath(), path);

            if (isDirectory)
            {
                if (!Directory.Exists(fullPath))
                {
                    Directory.CreateDirectory(fullPath);
                }
            }

            return fullPath;
        }

        public DriveInfo GetDrive()
        {
            return new DriveInfo(Path.GetPathRoot(GetBasePath()));
        }

        public void InitializeFsServer(LibHac.Horizon horizon, out HorizonClient fsServerClient)
        {
            LocalFileSystem serverBaseFs = new LocalFileSystem(GetBasePath());

            fsServerClient = horizon.CreatePrivilegedHorizonClient();
            var fsServer = new FileSystemServer(fsServerClient);

            DefaultFsServerObjects fsServerObjects = DefaultFsServerObjects.GetDefaultEmulatedCreators(serverBaseFs, KeySet, fsServer);

            // Use our own encrypted fs creator that always uses all-zero keys
            fsServerObjects.FsCreators.EncryptedFileSystemCreator = new EncryptedFileSystemCreator();

            GameCard = fsServerObjects.GameCard;
            SdCard = fsServerObjects.SdCard;

            SdCard.SetSdCardInsertionStatus(true);

            var fsServerConfig = new FileSystemServerConfig
            {
                DeviceOperator = fsServerObjects.DeviceOperator,
                ExternalKeySet = KeySet.ExternalKeySet,
                FsCreators = fsServerObjects.FsCreators
            };

            FileSystemServerInitializer.InitializeWithConfig(fsServerClient, fsServer, fsServerConfig);
        }

        public void ReloadKeySet()
        {
            KeySet ??= KeySet.CreateDefaultKeySet();

            string keyFile = null;
            string titleKeyFile = null;
            string consoleKeyFile = null;

            if (AppDataManager.Mode == AppDataManager.LaunchMode.UserProfile)
            {
                LoadSetAtPath(AppDataManager.KeysDirPathUser);
            }

            LoadSetAtPath(AppDataManager.KeysDirPath);

            void LoadSetAtPath(string basePath)
            {
                string localKeyFile = Path.Combine(basePath, "prod.keys");
                string localTitleKeyFile = Path.Combine(basePath, "title.keys");
                string localConsoleKeyFile = Path.Combine(basePath, "console.keys");

                if (File.Exists(localKeyFile))
                {
                    keyFile = localKeyFile;
                }

                if (File.Exists(localTitleKeyFile))
                {
                    titleKeyFile = localTitleKeyFile;
                }

                if (File.Exists(localConsoleKeyFile))
                {
                    consoleKeyFile = localConsoleKeyFile;
                }
            }

            ExternalKeyReader.ReadKeyFile(KeySet, keyFile, titleKeyFile, consoleKeyFile, null);
        }

        public void ImportTickets(IFileSystem fs)
        {
            foreach (DirectoryEntryEx ticketEntry in fs.EnumerateEntries("/", "*.tik"))
            {
                using var ticketFile = new UniqueRef<IFile>();

                Result result = fs.OpenFile(ref ticketFile.Ref(), ticketEntry.FullPath.ToU8Span(), OpenMode.Read);

                if (result.IsSuccess())
                {
                    Ticket ticket = new Ticket(ticketFile.Get.AsStream());

                    if (ticket.TitleKeyType == TitleKeyType.Common)
                    {
                        KeySet.ExternalKeySet.Add(new RightsId(ticket.RightsId), new AccessKey(ticket.GetTitleKey(KeySet)));
                    }
                }
            }
        }

        // Save data created before we supported extra data in directory save data will not work properly if
        // given empty extra data. Luckily some of that extra data can be created using the data from the
        // save data indexer, which should be enough to check access permissions for user saves.
        // Every single save data's extra data will be checked and fixed if needed each time the emulator is opened.
        // Consider removing this at some point in the future when we don't need to worry about old saves.
        public static Result FixExtraData(HorizonClient hos)
        {
            Result rc = GetSystemSaveList(hos, out List<ulong> systemSaveIds);
            if (rc.IsFailure()) return rc;

            rc = FixUnindexedSystemSaves(hos, systemSaveIds);
            if (rc.IsFailure()) return rc;

            rc = FixExtraDataInSpaceId(hos, SaveDataSpaceId.System);
            if (rc.IsFailure()) return rc;

            rc = FixExtraDataInSpaceId(hos, SaveDataSpaceId.User);
            if (rc.IsFailure()) return rc;

            return Result.Success;
        }

        private static Result FixExtraDataInSpaceId(HorizonClient hos, SaveDataSpaceId spaceId)
        {
            Span<SaveDataInfo> info = stackalloc SaveDataInfo[8];

            using var iterator = new UniqueRef<SaveDataIterator>();

            Result rc = hos.Fs.OpenSaveDataIterator(ref iterator.Ref(), spaceId);
            if (rc.IsFailure()) return rc;

            while (true)
            {
                rc = iterator.Get.ReadSaveDataInfo(out long count, info);
                if (rc.IsFailure()) return rc;

                if (count == 0)
                    return Result.Success;

                for (int i = 0; i < count; i++)
                {
                    rc = FixExtraData(out bool wasFixNeeded, hos, in info[i]);

                    if (ResultFs.TargetNotFound.Includes(rc))
                    {
                        // If the save wasn't found, try to create the directory for its save data ID
                        rc = CreateSaveDataDirectory(hos, in info[i]);

                        if (rc.IsFailure())
                        {
                            Logger.Warning?.Print(LogClass.Application, $"Error {rc.ToStringWithName()} when creating save data 0x{info[i].SaveDataId:x} in the {spaceId} save data space");

                            // Don't bother fixing the extra data if we couldn't create the directory
                            continue;
                        }

                        Logger.Info?.Print(LogClass.Application, $"Recreated directory for save data 0x{info[i].SaveDataId:x} in the {spaceId} save data space");

                        // Try to fix the extra data in the new directory
                        rc = FixExtraData(out wasFixNeeded, hos, in info[i]);
                    }

                    if (rc.IsFailure())
                    {
                        Logger.Warning?.Print(LogClass.Application, $"Error {rc.ToStringWithName()} when fixing extra data for save data 0x{info[i].SaveDataId:x} in the {spaceId} save data space");
                    }
                    else if (wasFixNeeded)
                    {
                        Logger.Info?.Print(LogClass.Application, $"Fixed extra data for save data 0x{info[i].SaveDataId:x} in the {spaceId} save data space");
                    }
                }
            }
        }

        private static Result CreateSaveDataDirectory(HorizonClient hos, in SaveDataInfo info)
        {
            if (info.SpaceId != SaveDataSpaceId.User && info.SpaceId != SaveDataSpaceId.System)
                return Result.Success;

            const string mountName = "SaveDir";
            var mountNameU8 = mountName.ToU8Span();

            BisPartitionId partitionId = info.SpaceId switch
            {
                SaveDataSpaceId.System => BisPartitionId.System,
                SaveDataSpaceId.User => BisPartitionId.User,
                _ => throw new ArgumentOutOfRangeException()
            };

            Result rc = hos.Fs.MountBis(mountNameU8, partitionId);
            if (rc.IsFailure()) return rc;
            try
            {
                var path = $"{mountName}:/save/{info.SaveDataId:x16}".ToU8Span();

                rc = hos.Fs.GetEntryType(out _, path);

                if (ResultFs.PathNotFound.Includes(rc))
                {
                    rc = hos.Fs.CreateDirectory(path);
                }

                return rc;
            }
            finally
            {
                hos.Fs.Unmount(mountNameU8);
            }
        }

        // Gets a list of all the save data files or directories in the system partition.
        private static Result GetSystemSaveList(HorizonClient hos, out List<ulong> list)
        {
            list = null;

            var mountName = "system".ToU8Span();
            DirectoryHandle handle = default;
            List<ulong> localList = new List<ulong>();

            try
            {
                Result rc = hos.Fs.MountBis(mountName, BisPartitionId.System);
                if (rc.IsFailure()) return rc;

                rc = hos.Fs.OpenDirectory(out handle, "system:/save".ToU8Span(), OpenDirectoryMode.All);
                if (rc.IsFailure()) return rc;

                DirectoryEntry entry = new DirectoryEntry();

                while (true)
                {
                    rc = hos.Fs.ReadDirectory(out long readCount, SpanHelpers.AsSpan(ref entry), handle);
                    if (rc.IsFailure()) return rc;

                    if (readCount == 0)
                        break;

                    if (Utf8Parser.TryParse(entry.Name, out ulong saveDataId, out int bytesRead, 'x') &&
                        bytesRead == 16 && (long)saveDataId < 0)
                    {
                        localList.Add(saveDataId);
                    }
                }

                list = localList;

                return Result.Success;
            }
            finally
            {
                if (handle.IsValid)
                {
                    hos.Fs.CloseDirectory(handle);
                }

                if (hos.Fs.IsMounted(mountName))
                {
                    hos.Fs.Unmount(mountName);
                }
            }
        }

        // Adds system save data that isn't in the save data indexer to the indexer and creates extra data for it.
        // Only save data IDs added to SystemExtraDataFixInfo will be fixed.
        private static Result FixUnindexedSystemSaves(HorizonClient hos, List<ulong> existingSaveIds)
        {
            foreach (var fixInfo in SystemExtraDataFixInfo)
            {
                if (!existingSaveIds.Contains(fixInfo.StaticSaveDataId))
                {
                    continue;
                }

                Result rc = FixSystemExtraData(out bool wasFixNeeded, hos, in fixInfo);

                if (rc.IsFailure())
                {
                    Logger.Warning?.Print(LogClass.Application,
                        $"Error {rc.ToStringWithName()} when fixing extra data for system save data 0x{fixInfo.StaticSaveDataId:x}");
                }
                else if (wasFixNeeded)
                {
                    Logger.Info?.Print(LogClass.Application,
                        $"Tried to rebuild extra data for system save data 0x{fixInfo.StaticSaveDataId:x}");
                }
            }

            return Result.Success;
        }

        private static Result FixSystemExtraData(out bool wasFixNeeded, HorizonClient hos, in ExtraDataFixInfo info)
        {
            wasFixNeeded = true;

            Result rc = hos.Fs.Impl.ReadSaveDataFileSystemExtraData(out SaveDataExtraData extraData, info.StaticSaveDataId);
            if (!rc.IsSuccess())
            {
                if (!ResultFs.TargetNotFound.Includes(rc))
                    return rc;

                // We'll reach this point only if the save data directory exists but it's not in the save data indexer.
                // Creating the save will add it to the indexer while leaving its existing contents intact.
                return hos.Fs.CreateSystemSaveData(info.StaticSaveDataId, UserId.InvalidId, info.OwnerId, info.DataSize,
                    info.JournalSize, info.Flags);
            }

            if (extraData.Attribute.StaticSaveDataId != 0 && extraData.OwnerId != 0)
            {
                wasFixNeeded = false;
                return Result.Success;
            }

            extraData = new SaveDataExtraData
            {
                Attribute = { StaticSaveDataId = info.StaticSaveDataId },
                OwnerId = info.OwnerId,
                Flags = info.Flags,
                DataSize = info.DataSize,
                JournalSize = info.JournalSize
            };

            // Make a mask for writing the entire extra data
            Unsafe.SkipInit(out SaveDataExtraData extraDataMask);
            SpanHelpers.AsByteSpan(ref extraDataMask).Fill(0xFF);

            return hos.Fs.Impl.WriteSaveDataFileSystemExtraData(SaveDataSpaceId.System, info.StaticSaveDataId,
                in extraData, in extraDataMask);
        }

        private static Result FixExtraData(out bool wasFixNeeded, HorizonClient hos, in SaveDataInfo info)
        {
            wasFixNeeded = true;

            Result rc = hos.Fs.Impl.ReadSaveDataFileSystemExtraData(out SaveDataExtraData extraData, info.SpaceId,
                info.SaveDataId);
            if (rc.IsFailure()) return rc;

            // The extra data should have program ID or static save data ID set if it's valid.
            // We only try to fix the extra data if the info from the save data indexer has a program ID or static save data ID.
            bool canFixByProgramId = extraData.Attribute.ProgramId == ProgramId.InvalidId &&
                                       info.ProgramId != ProgramId.InvalidId;

            bool canFixBySaveDataId = extraData.Attribute.StaticSaveDataId == 0 && info.StaticSaveDataId != 0;

            if (!canFixByProgramId && !canFixBySaveDataId)
            {
                wasFixNeeded = false;
                return Result.Success;
            }

            // The save data attribute struct can be completely created from the save data info.
            extraData.Attribute.ProgramId = info.ProgramId;
            extraData.Attribute.UserId = info.UserId;
            extraData.Attribute.StaticSaveDataId = info.StaticSaveDataId;
            extraData.Attribute.Type = info.Type;
            extraData.Attribute.Rank = info.Rank;
            extraData.Attribute.Index = info.Index;

            // The rest of the extra data can't be created from the save data info.
            // On user saves the owner ID will almost certainly be the same as the program ID.
            if (info.Type != LibHac.Fs.SaveDataType.System)
            {
                extraData.OwnerId = info.ProgramId.Value;
            }
            else
            {
                // Try to match the system save with one of the known saves
                foreach (ExtraDataFixInfo fixInfo in SystemExtraDataFixInfo)
                {
                    if (extraData.Attribute.StaticSaveDataId == fixInfo.StaticSaveDataId)
                    {
                        extraData.OwnerId = fixInfo.OwnerId;
                        extraData.Flags = fixInfo.Flags;
                        extraData.DataSize = fixInfo.DataSize;
                        extraData.JournalSize = fixInfo.JournalSize;

                        break;
                    }
                }
            }

            // Make a mask for writing the entire extra data
            Unsafe.SkipInit(out SaveDataExtraData extraDataMask);
            SpanHelpers.AsByteSpan(ref extraDataMask).Fill(0xFF);

            return hos.Fs.Impl.WriteSaveDataFileSystemExtraData(info.SpaceId, info.SaveDataId, in extraData, in extraDataMask);
        }

        struct ExtraDataFixInfo
        {
            public ulong StaticSaveDataId;
            public ulong OwnerId;
            public SaveDataFlags Flags;
            public long DataSize;
            public long JournalSize;
        }

        private static readonly ExtraDataFixInfo[] SystemExtraDataFixInfo =
        {
            new ExtraDataFixInfo()
            {
                StaticSaveDataId = 0x8000000000000030,
                OwnerId = 0x010000000000001F,
                Flags = SaveDataFlags.KeepAfterResettingSystemSaveDataWithoutUserSaveData,
                DataSize = 0x10000,
                JournalSize = 0x10000
            },
            new ExtraDataFixInfo()
            {
                StaticSaveDataId = 0x8000000000001040,
                OwnerId = 0x0100000000001009,
                Flags = SaveDataFlags.None,
                DataSize = 0xC000,
                JournalSize = 0xC000
            }
        };

        public void Unload()
        {
            RomFs?.Dispose();
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Unload();
            }
        }

        public static VirtualFileSystem CreateInstance()
        {
            if (_isInitialized)
            {
                throw new InvalidOperationException("VirtualFileSystem can only be instantiated once!");
            }

            _isInitialized = true;

            return new VirtualFileSystem();
        }
    }
}