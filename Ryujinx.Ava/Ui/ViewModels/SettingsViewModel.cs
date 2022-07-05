using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Threading;
using LibHac.Tools.FsSystem;
using Ryujinx.Audio.Backends.OpenAL;
using Ryujinx.Audio.Backends.SDL2;
using Ryujinx.Audio.Backends.SoundIo;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Input;
using Ryujinx.Ava.Ui.Controls;
using Ryujinx.Ava.Ui.Windows;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Common.GraphicsDriver;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.FileSystem;
using Ryujinx.HLE.HOS.Services.Time.TimeZone;
using Ryujinx.Input;
using Ryujinx.Ui.Common.Configuration;
using Ryujinx.Ui.Common.Configuration.System;
using System;
using System.Collections.Generic;
using TimeZone = Ryujinx.Ava.Ui.Models.TimeZone;

namespace Ryujinx.Ava.Ui.ViewModels
{
    internal class SettingsViewModel : BaseModel
    {
        private readonly VirtualFileSystem _virtualFileSystem;
        private readonly ContentManager _contentManager;
        private readonly StyleableWindow _owner;
        private TimeZoneContentManager _timeZoneContentManager;

        private readonly List<string> _validTzRegions;

        private float _customResolutionScale;
        private int _resolutionScale;
        private int _graphicsBackendMultithreadingIndex;
        private float _previousVolumeLevel;
        private float _volume;

        public int ResolutionScale
        {
            get => _resolutionScale;
            set
            {
                _resolutionScale = value;

                OnPropertyChanged(nameof(CustomResolutionScale));
                OnPropertyChanged(nameof(IsCustomResolutionScaleActive));
            }
        }
        public int GraphicsBackendMultithreadingIndex
        {
            get => _graphicsBackendMultithreadingIndex;
            set
            {
                _graphicsBackendMultithreadingIndex = value;

                if (_owner != null)
                {
                    if (_graphicsBackendMultithreadingIndex != (int)ConfigurationState.Instance.Graphics.BackendThreading.Value)
                    {
                        Dispatcher.UIThread.Post(async () =>
                        {
                            await ContentDialogHelper.CreateInfoDialog(_owner,
                                                                       LocaleManager.Instance["DialogSettingsBackendThreadingWarningMessage"],
                                                                       "",
                                                                       "",
                                                                       LocaleManager.Instance["InputDialogOk"],
                                                                       LocaleManager.Instance["DialogSettingsBackendThreadingWarningTitle"]);
                        });
                    }
                }

                OnPropertyChanged();
            }
        }

        public float CustomResolutionScale
        {
            get => _customResolutionScale;
            set
            {
                _customResolutionScale = MathF.Round(value, 1);

                OnPropertyChanged();
            }
        }

        public bool EnableDiscordIntegration { get; set; }
        public bool CheckUpdatesOnStart { get; set; }
        public bool ShowConfirmExit { get; set; }
        public bool HideCursorOnIdle { get; set; }
        public bool EnableDockedMode { get; set; }
        public bool EnableKeyboard { get; set; }
        public bool EnableMouse { get; set; }
        public bool EnableVsync { get; set; }
        public bool EnablePptc { get; set; }
        public bool EnableInternetAccess { get; set; }
        public bool EnableFsIntegrityChecks { get; set; }
        public bool IgnoreMissingServices { get; set; }
        public bool ExpandDramSize { get; set; }
        public bool EnableShaderCache { get; set; }
        public bool EnableFileLog { get; set; }
        public bool EnableStub { get; set; }
        public bool EnableInfo { get; set; }
        public bool EnableWarn { get; set; }
        public bool EnableError { get; set; }
        public bool EnableTrace { get; set; }
        public bool EnableGuest { get; set; }
        public bool EnableFsAccessLog { get; set; }
        public bool EnableDebug { get; set; }
        public bool IsOpenAlEnabled { get; set; }
        public bool IsSoundIoEnabled { get; set; }
        public bool IsSDL2Enabled { get; set; }
        public bool EnableCustomTheme { get; set; }
        public bool IsCustomResolutionScaleActive => _resolutionScale == 0;

        public string TimeZone { get; set; }
        public string ShaderDumpPath { get; set; }
        public string CustomThemePath { get; set; }

        public int Language { get; set; }
        public int Region { get; set; }
        public int FsGlobalAccessLogMode { get; set; }
        public int AudioBackend { get; set; }
        public int MaxAnisotropy { get; set; }
        public int AspectRatio { get; set; }
        public int OpenglDebugLevel { get; set; }
        public int MemoryMode { get; set; }
        public int BaseStyleIndex { get; set; }

        public float Volume
        {
            get => _volume;
            set
            {
                _volume = value;

                ConfigurationState.Instance.System.AudioVolume.Value = (float)(_volume / 100);

                OnPropertyChanged();
            }
        }

        public DateTimeOffset DateOffset { get; set; }
        public TimeSpan TimeOffset { get; set; }
        public AvaloniaList<TimeZone> TimeZones { get; set; }

        public AvaloniaList<string> GameDirectories { get; set; }

        private KeyboardHotkeys _keyboardHotkeys;

        public KeyboardHotkeys KeyboardHotkeys
        {
            get => _keyboardHotkeys;
            set
            {
                _keyboardHotkeys = value;

                OnPropertyChanged();
            }
        }

        public IGamepadDriver AvaloniaKeyboardDriver { get; }

        public SettingsViewModel(VirtualFileSystem virtualFileSystem, ContentManager contentManager, StyleableWindow owner) : this()
        {
            _virtualFileSystem = virtualFileSystem;
            _contentManager = contentManager;
            _owner = owner;
            if (Program.PreviewerDetached)
            {
                LoadTimeZones();
                AvaloniaKeyboardDriver = new AvaloniaKeyboardDriver(owner);
            }
        }

        public SettingsViewModel()
        {
            GameDirectories = new AvaloniaList<string>();
            TimeZones = new AvaloniaList<TimeZone>();
            _validTzRegions = new List<string>();

            CheckSoundBackends();

            if (Program.PreviewerDetached)
            {
                LoadCurrentConfiguration();
            }
        }

        public void CheckSoundBackends()
        {
            IsOpenAlEnabled = OpenALHardwareDeviceDriver.IsSupported;
            IsSoundIoEnabled = SoundIoHardwareDeviceDriver.IsSupported;
            IsSDL2Enabled = SDL2HardwareDeviceDriver.IsSupported;
        }

        public void LoadTimeZones()
        {
            _timeZoneContentManager = new TimeZoneContentManager();

            _timeZoneContentManager.InitializeInstance(_virtualFileSystem, _contentManager, IntegrityCheckLevel.None);

            foreach ((int offset, string location, string abbr) in _timeZoneContentManager.ParseTzOffsets())
            {
                int hours = Math.DivRem(offset, 3600, out int seconds);
                int minutes = Math.Abs(seconds) / 60;

                string abbr2 = abbr.StartsWith('+') || abbr.StartsWith('-') ? string.Empty : abbr;

                TimeZones.Add(new TimeZone($"UTC{hours:+0#;-0#;+00}:{minutes:D2}", location, abbr2));

                _validTzRegions.Add(location);
            }
        }

        public void ValidateAndSetTimeZone(string location)
        {
            if (_validTzRegions.Contains(location))
            {
                TimeZone = location;

                OnPropertyChanged(nameof(TimeZone));
            }
        }

        public async void BrowseTheme()
        {
            var dialog = new OpenFileDialog()
            {
                Title = LocaleManager.Instance["SettingsSelectThemeFileDialogTitle"],
                AllowMultiple = false
            };

            dialog.Filters.Add(new FileDialogFilter() { Extensions = { "xaml" }, Name = LocaleManager.Instance["SettingsXamlThemeFile"] });

            var file = await dialog.ShowAsync(_owner);

            if (file != null && file.Length > 0)
            {
                CustomThemePath = file[0];
                OnPropertyChanged(nameof(CustomThemePath));
            }
        }

        public void LoadCurrentConfiguration()
        {
            ConfigurationState config = ConfigurationState.Instance;

            GameDirectories.Clear();
            GameDirectories.AddRange(config.Ui.GameDirs.Value);

            EnableDiscordIntegration = config.EnableDiscordIntegration;
            CheckUpdatesOnStart = config.CheckUpdatesOnStart;
            ShowConfirmExit = config.ShowConfirmExit;
            HideCursorOnIdle = config.HideCursorOnIdle;
            EnableDockedMode = config.System.EnableDockedMode;
            EnableKeyboard = config.Hid.EnableKeyboard;
            EnableMouse = config.Hid.EnableMouse;
            EnableVsync = config.Graphics.EnableVsync;
            EnablePptc = config.System.EnablePtc;
            EnableInternetAccess = config.System.EnableInternetAccess;
            EnableFsIntegrityChecks = config.System.EnableFsIntegrityChecks;
            IgnoreMissingServices = config.System.IgnoreMissingServices;
            ExpandDramSize = config.System.ExpandRam;
            EnableShaderCache = config.Graphics.EnableShaderCache;
            EnableFileLog = config.Logger.EnableFileLog;
            EnableStub = config.Logger.EnableStub;
            EnableInfo = config.Logger.EnableInfo;
            EnableWarn = config.Logger.EnableWarn;
            EnableError = config.Logger.EnableError;
            EnableTrace = config.Logger.EnableTrace;
            EnableGuest = config.Logger.EnableGuest;
            EnableDebug = config.Logger.EnableDebug;
            EnableFsAccessLog = config.Logger.EnableFsAccessLog;
            EnableCustomTheme = config.Ui.EnableCustomTheme;
            Volume = config.System.AudioVolume * 100;

            GraphicsBackendMultithreadingIndex = (int)config.Graphics.BackendThreading.Value;

            OpenglDebugLevel = (int)config.Logger.GraphicsDebugLevel.Value;

            TimeZone = config.System.TimeZone;
            ShaderDumpPath = config.Graphics.ShadersDumpPath;
            CustomThemePath = config.Ui.CustomThemePath;
            BaseStyleIndex = config.Ui.BaseStyle == "Light" ? 0 : 1;

            Language = (int)config.System.Language.Value;
            Region = (int)config.System.Region.Value;
            FsGlobalAccessLogMode = config.System.FsGlobalAccessLogMode;
            AudioBackend = (int)config.System.AudioBackend.Value;
            MemoryMode = (int)config.System.MemoryManagerMode.Value;

            float anisotropy = config.Graphics.MaxAnisotropy;

            MaxAnisotropy = anisotropy == -1 ? 0 : (int)(MathF.Log2(anisotropy));
            AspectRatio = (int)config.Graphics.AspectRatio.Value;

            int resolution = config.Graphics.ResScale;

            ResolutionScale = resolution == -1 ? 0 : resolution;
            CustomResolutionScale = config.Graphics.ResScaleCustom;

            DateTime dateTimeOffset = DateTime.Now.AddSeconds(config.System.SystemTimeOffset);

            DateOffset = dateTimeOffset.Date;
            TimeOffset = dateTimeOffset.TimeOfDay;

            KeyboardHotkeys = config.Hid.Hotkeys.Value;

            _previousVolumeLevel = Volume;
        }

        public void SaveSettings()
        {
            List<string> gameDirs = new List<string>(GameDirectories);

            ConfigurationState config = ConfigurationState.Instance;

            if (_validTzRegions.Contains(TimeZone))
            {
                config.System.TimeZone.Value = TimeZone;
            }

            config.Logger.EnableError.Value = EnableError;
            config.Logger.EnableTrace.Value = EnableTrace;
            config.Logger.EnableWarn.Value = EnableWarn;
            config.Logger.EnableInfo.Value = EnableInfo;
            config.Logger.EnableStub.Value = EnableStub;
            config.Logger.EnableDebug.Value = EnableDebug;
            config.Logger.EnableGuest.Value = EnableGuest;
            config.Logger.EnableFsAccessLog.Value = EnableFsAccessLog;
            config.Logger.EnableFileLog.Value = EnableFileLog;
            config.Logger.GraphicsDebugLevel.Value = (GraphicsDebugLevel)OpenglDebugLevel;
            config.System.EnableDockedMode.Value = EnableDockedMode;
            config.EnableDiscordIntegration.Value = EnableDiscordIntegration;
            config.CheckUpdatesOnStart.Value = CheckUpdatesOnStart;
            config.ShowConfirmExit.Value = ShowConfirmExit;
            config.HideCursorOnIdle.Value = HideCursorOnIdle;
            config.Graphics.EnableVsync.Value = EnableVsync;
            config.Graphics.EnableShaderCache.Value = EnableShaderCache;
            config.System.EnablePtc.Value = EnablePptc;
            config.System.EnableInternetAccess.Value = EnableInternetAccess;
            config.System.EnableFsIntegrityChecks.Value = EnableFsIntegrityChecks;
            config.System.IgnoreMissingServices.Value = IgnoreMissingServices;
            config.System.ExpandRam.Value = ExpandDramSize;
            config.Hid.EnableKeyboard.Value = EnableKeyboard;
            config.Hid.EnableMouse.Value = EnableMouse;
            config.Ui.CustomThemePath.Value = CustomThemePath;
            config.Ui.EnableCustomTheme.Value = EnableCustomTheme;
            config.Ui.BaseStyle.Value = BaseStyleIndex == 0 ? "Light" : "Dark";
            config.System.Language.Value = (Language)Language;
            config.System.Region.Value = (Region)Region;

            if (ConfigurationState.Instance.Graphics.BackendThreading != (BackendThreading)GraphicsBackendMultithreadingIndex)
            {
                DriverUtilities.ToggleOGLThreading(GraphicsBackendMultithreadingIndex == (int)BackendThreading.Off);
            }

            config.Graphics.BackendThreading.Value = (BackendThreading)GraphicsBackendMultithreadingIndex;

            TimeSpan systemTimeOffset = DateOffset - DateTime.Now;

            config.System.SystemTimeOffset.Value = systemTimeOffset.Seconds;
            config.Graphics.ShadersDumpPath.Value = ShaderDumpPath;
            config.Ui.GameDirs.Value = gameDirs;
            config.System.FsGlobalAccessLogMode.Value = FsGlobalAccessLogMode;
            config.System.MemoryManagerMode.Value = (MemoryManagerMode)MemoryMode;

            float anisotropy = MaxAnisotropy == 0 ? -1 : MathF.Pow(2, MaxAnisotropy);

            config.Graphics.MaxAnisotropy.Value = anisotropy;
            config.Graphics.AspectRatio.Value = (AspectRatio)AspectRatio;
            config.Graphics.ResScale.Value = ResolutionScale == 0 ? -1 : ResolutionScale;
            config.Graphics.ResScaleCustom.Value = CustomResolutionScale;
            config.System.AudioVolume.Value = Volume / 100;

            AudioBackend audioBackend = (AudioBackend)AudioBackend;
            if (audioBackend != config.System.AudioBackend.Value)
            {
                config.System.AudioBackend.Value = audioBackend;

                Logger.Info?.Print(LogClass.Application, $"AudioBackend toggled to: {audioBackend}");
            }

            config.Hid.Hotkeys.Value = KeyboardHotkeys;

            config.ToFileFormat().SaveConfig(Program.ConfigurationPath);

            MainWindow.UpdateGraphicsConfig();

            _previousVolumeLevel = Volume;
        }

        public void RevertIfNotSaved()
        {
            Program.ReloadConfig();
        }
    }
}