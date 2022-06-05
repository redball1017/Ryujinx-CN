using Gtk;

namespace Ryujinx.Ui.Widgets
{
    public partial class GameTableContextMenu : Menu
    {
        private MenuItem _openSaveUserDirMenuItem;
        private MenuItem _openSaveDeviceDirMenuItem;
        private MenuItem _openSaveBcatDirMenuItem;
        private MenuItem _manageTitleUpdatesMenuItem;
        private MenuItem _manageDlcMenuItem;
        private MenuItem _manageCheatMenuItem;
        private MenuItem _openTitleModDirMenuItem;
        private MenuItem _openTitleSdModDirMenuItem;
        private Menu     _extractSubMenu;
        private MenuItem _extractMenuItem;
        private MenuItem _extractRomFsMenuItem;
        private MenuItem _extractExeFsMenuItem;
        private MenuItem _extractLogoMenuItem;
        private Menu     _manageSubMenu;
        private MenuItem _manageCacheMenuItem;
        private MenuItem _purgePtcCacheMenuItem;
        private MenuItem _purgeShaderCacheMenuItem;
        private MenuItem _openPtcDirMenuItem;
        private MenuItem _openShaderCacheDirMenuItem;

        private void InitializeComponent()
        {
            //
            // _openSaveUserDirMenuItem
            //
            _openSaveUserDirMenuItem = new MenuItem("打开用户保存目录")
            {
                TooltipText = "打开包含应用程序用户保存的目录."
            };
            _openSaveUserDirMenuItem.Activated += OpenSaveUserDir_Clicked;

            //
            // _openSaveDeviceDirMenuItem
            //
            _openSaveDeviceDirMenuItem = new MenuItem("打开设备保存目录")
            {
                TooltipText = "打开包含应用程序设备保存的目录."
            };
            _openSaveDeviceDirMenuItem.Activated += OpenSaveDeviceDir_Clicked;

            //
            // _openSaveBcatDirMenuItem
            //
            _openSaveBcatDirMenuItem = new MenuItem("打开BCAT保存目录")
            {
                TooltipText = "打开包含应用程序BCAT保存的目录."
            };
            _openSaveBcatDirMenuItem.Activated += OpenSaveBcatDir_Clicked;

            //
            // _manageTitleUpdatesMenuItem
            //
            _manageTitleUpdatesMenuItem = new MenuItem("管理更新")
            {
                TooltipText = "打开“更新管理”窗口"
            };
            _manageTitleUpdatesMenuItem.Activated += ManageTitleUpdates_Clicked;

            //
            // _manageDlcMenuItem
            //
            _manageDlcMenuItem = new MenuItem("管理DLC")
            {
                TooltipText = "打开DLC管理窗口"
            };
            _manageDlcMenuItem.Activated += ManageDlc_Clicked;

            //
            // _manageCheatMenuItem
            //
            _manageCheatMenuItem = new MenuItem("管理作弊码")
            {
                TooltipText = "打开作弊码管理窗口"
            };
            _manageCheatMenuItem.Activated += ManageCheats_Clicked;

            //
            // _openTitleModDirMenuItem
            //
            _openTitleModDirMenuItem = new MenuItem("打开模组目录")
            {
                TooltipText = "打开包含应用程序模组的目录."
            };
            _openTitleModDirMenuItem.Activated += OpenTitleModDir_Clicked;

            //
            // _openTitleSdModDirMenuItem
            //
            _openTitleSdModDirMenuItem = new MenuItem("打开大气层模组目录")
            {
                TooltipText = "打开包含应用程序Mods的备用SD卡大气层目录."
            };
            _openTitleSdModDirMenuItem.Activated += OpenTitleSdModDir_Clicked;

            //
            // _extractSubMenu
            //
            _extractSubMenu = new Menu();

            //
            // _extractMenuItem
            //
            _extractMenuItem = new MenuItem("提取数据")
            {
                Submenu = _extractSubMenu
            };

            //
            // _extractRomFsMenuItem
            //
            _extractRomFsMenuItem = new MenuItem("RomFS")
            {
                TooltipText = "从应用程序的当前配置（包括更新）中提取RomFS部分."
            };
            _extractRomFsMenuItem.Activated += ExtractRomFs_Clicked;

            //
            // _extractExeFsMenuItem
            //
            _extractExeFsMenuItem = new MenuItem("ExeFS")
            {
                TooltipText = "从应用程序的当前配置（包括更新）中提取ExeFS部分."
            };
            _extractExeFsMenuItem.Activated += ExtractExeFs_Clicked;

            //
            // _extractLogoMenuItem
            //
            _extractLogoMenuItem = new MenuItem("Logo")
            {
                TooltipText = "从应用程序的当前配置（包括更新）中提取徽标部分."
            };
            _extractLogoMenuItem.Activated += ExtractLogo_Clicked;

            //
            // _manageSubMenu
            //
            _manageSubMenu = new Menu();

            //
            // _manageCacheMenuItem
            //
            _manageCacheMenuItem = new MenuItem("缓存管理")
            {
                Submenu = _manageSubMenu
            };

            //
            // _purgePtcCacheMenuItem
            //
            _purgePtcCacheMenuItem = new MenuItem("清除PPTC缓存")
            {
                TooltipText = "删除应用程序的PPTC缓存."
            };
            _purgePtcCacheMenuItem.Activated += PurgePtcCache_Clicked;

            //
            // _purgeShaderCacheMenuItem
            //
            _purgeShaderCacheMenuItem = new MenuItem("清除着色器缓存")
            {
                TooltipText = "删除应用程序的着色器缓存."
            };
            _purgeShaderCacheMenuItem.Activated += PurgeShaderCache_Clicked;

            //
            // _openPtcDirMenuItem
            //
            _openPtcDirMenuItem = new MenuItem("打开PPTC目录")
            {
                TooltipText = "打开包含应用程序PPTC缓存的目录."
            };
            _openPtcDirMenuItem.Activated += OpenPtcDir_Clicked;

            //
            // _openShaderCacheDirMenuItem
            //
            _openShaderCacheDirMenuItem = new MenuItem("打开着色器缓存目录")
            {
                TooltipText = "打开包含应用程序着色器缓存的目录."
            };
            _openShaderCacheDirMenuItem.Activated += OpenShaderCacheDir_Clicked;

            ShowComponent();
        }

        private void ShowComponent()
        {
            _extractSubMenu.Append(_extractExeFsMenuItem);
            _extractSubMenu.Append(_extractRomFsMenuItem);
            _extractSubMenu.Append(_extractLogoMenuItem);

            _manageSubMenu.Append(_purgePtcCacheMenuItem);
            _manageSubMenu.Append(_purgeShaderCacheMenuItem);
            _manageSubMenu.Append(_openPtcDirMenuItem);
            _manageSubMenu.Append(_openShaderCacheDirMenuItem);

            Add(_openSaveUserDirMenuItem);
            Add(_openSaveDeviceDirMenuItem);
            Add(_openSaveBcatDirMenuItem);
            Add(new SeparatorMenuItem());
            Add(_manageTitleUpdatesMenuItem);
            Add(_manageDlcMenuItem);
            Add(_manageCheatMenuItem);
            Add(_openTitleModDirMenuItem);
            Add(_openTitleSdModDirMenuItem);
            Add(new SeparatorMenuItem());
            Add(_manageCacheMenuItem);
            Add(_extractMenuItem);

            ShowAll();
        }
    }
}