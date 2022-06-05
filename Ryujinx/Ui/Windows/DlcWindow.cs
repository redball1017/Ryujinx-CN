using Gtk;
using LibHac.Common;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.FsSystem;
using LibHac.Tools.Fs;
using LibHac.Tools.FsSystem;
using LibHac.Tools.FsSystem.NcaUtils;
using Ryujinx.Common.Configuration;
using Ryujinx.HLE.FileSystem;
using Ryujinx.Ui.Widgets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using GUI        = Gtk.Builder.ObjectAttribute;
using JsonHelper = Ryujinx.Common.Utilities.JsonHelper;

namespace Ryujinx.Ui.Windows
{
    public class DlcWindow : Window
    {
        private readonly VirtualFileSystem  _virtualFileSystem;
        private readonly string             _titleId;
        private readonly string             _dlcJsonPath;
        private readonly List<DlcContainer> _dlcContainerList;

#pragma warning disable CS0649, IDE0044
        [GUI] Label         _baseTitleInfoLabel;
        [GUI] TreeView      _dlcTreeView;
        [GUI] TreeSelection _dlcTreeSelection;
#pragma warning restore CS0649, IDE0044

        public DlcWindow(VirtualFileSystem virtualFileSystem, string titleId, string titleName) : this(new Builder("Ryujinx.Ui.Windows.DlcWindow.glade"), virtualFileSystem, titleId, titleName) { }

        private DlcWindow(Builder builder, VirtualFileSystem virtualFileSystem, string titleId, string titleName) : base(builder.GetObject("_dlcWindow").Handle)
        {
            builder.Autoconnect(this);

            _titleId                 = titleId;
            _virtualFileSystem       = virtualFileSystem;
            _dlcJsonPath             = System.IO.Path.Combine(AppDataManager.GamesDirPath, _titleId, "dlc.json");
            _baseTitleInfoLabel.Text = $"可用于 {titleName} [{titleId.ToUpper()}] 的DLC";

            try
            {
                _dlcContainerList = JsonHelper.DeserializeFromFile<List<DlcContainer>>(_dlcJsonPath);
            }
            catch
            {
                _dlcContainerList = new List<DlcContainer>();
            }
            
            _dlcTreeView.Model = new TreeStore(typeof(bool), typeof(string), typeof(string));

            CellRendererToggle enableToggle = new CellRendererToggle();
            enableToggle.Toggled += (sender, args) =>
            {
                _dlcTreeView.Model.GetIter(out TreeIter treeIter, new TreePath(args.Path));
                bool newValue = !(bool)_dlcTreeView.Model.GetValue(treeIter, 0);
                _dlcTreeView.Model.SetValue(treeIter, 0, newValue);

                if (_dlcTreeView.Model.IterChildren(out TreeIter childIter, treeIter))
                {
                    do
                    {
                        _dlcTreeView.Model.SetValue(childIter, 0, newValue);
                    }
                    while (_dlcTreeView.Model.IterNext(ref childIter));
                }
            };

            _dlcTreeView.AppendColumn("Enabled", enableToggle,           "active", 0);
            _dlcTreeView.AppendColumn("TitleId", new CellRendererText(), "text",   1);
            _dlcTreeView.AppendColumn("Path",    new CellRendererText(), "text",   2);

            foreach (DlcContainer dlcContainer in _dlcContainerList)
            {
                if (File.Exists(dlcContainer.Path))
                {
                    // The parent tree item has its own "enabled" check box, but it's the actual
                    // nca entries that store the enabled / disabled state. A bit of a UI inconsistency.
                    // Maybe a tri-state check box would be better, but for now we check the parent
                    // "enabled" box if all child NCAs are enabled. Usually fine since each nsp has only one nca.
                    bool areAllContentPacksEnabled = dlcContainer.DlcNcaList.TrueForAll((nca) => nca.Enabled);
                    TreeIter parentIter = ((TreeStore)_dlcTreeView.Model).AppendValues(areAllContentPacksEnabled, "", dlcContainer.Path);
                    using FileStream containerFile = File.OpenRead(dlcContainer.Path);
                    PartitionFileSystem pfs = new PartitionFileSystem(containerFile.AsStorage());
                    _virtualFileSystem.ImportTickets(pfs);

                    foreach (DlcNca dlcNca in dlcContainer.DlcNcaList)
                    {
                        using var ncaFile = new UniqueRef<IFile>();

                        pfs.OpenFile(ref ncaFile.Ref(), dlcNca.Path.ToU8Span(), OpenMode.Read).ThrowIfFailure();
                        Nca nca = TryCreateNca(ncaFile.Get.AsStorage(), dlcContainer.Path);

                        if (nca != null)
                        {
                            ((TreeStore)_dlcTreeView.Model).AppendValues(parentIter, dlcNca.Enabled, nca.Header.TitleId.ToString("X16"), dlcNca.Path);
                        }
                    }
                }
                else
                {
                    // DLC file moved or renamed. Allow the user to remove it without crashing the whole dialog.
                    TreeIter parentIter = ((TreeStore)_dlcTreeView.Model).AppendValues(false, "", $"(MISSING) {dlcContainer.Path}");
                }
            }
        }

        private Nca TryCreateNca(IStorage ncaStorage, string containerPath)
        {
            try
            {
                return new Nca(_virtualFileSystem.KeySet, ncaStorage);
            }
            catch (Exception exception)
            {
                GtkDialog.CreateErrorDialog($"{exception.Message}. 错误文件: {containerPath}");
            }

            return null;
        }

        private void AddButton_Clicked(object sender, EventArgs args)
        {
            FileChooserNative fileChooser = new FileChooserNative("选择DLC文件", this, FileChooserAction.Open, "添加", "返回")
            {
                SelectMultiple = true
            };

            FileFilter filter = new FileFilter()
            {
                Name = "Switch游戏DLCs"
            };
            filter.AddPattern("*.nsp");

            fileChooser.AddFilter(filter);

            if (fileChooser.Run() == (int)ResponseType.Accept)
            {
                foreach (string containerPath in fileChooser.Filenames)
                {
                    if (!File.Exists(containerPath))
                    {
                        return;
                    }

                    using (FileStream containerFile = File.OpenRead(containerPath))
                    {
                        PartitionFileSystem pfs = new PartitionFileSystem(containerFile.AsStorage());
                        bool containsDlc = false;

                        _virtualFileSystem.ImportTickets(pfs);

                        TreeIter? parentIter = null;

                        foreach (DirectoryEntryEx fileEntry in pfs.EnumerateEntries("/", "*.nca"))
                        {
                            using var ncaFile = new UniqueRef<IFile>();

                            pfs.OpenFile(ref ncaFile.Ref(), fileEntry.FullPath.ToU8Span(), OpenMode.Read).ThrowIfFailure();

                            Nca nca = TryCreateNca(ncaFile.Get.AsStorage(), containerPath);

                            if (nca == null) continue;

                            if (nca.Header.ContentType == NcaContentType.PublicData)
                            {
                                if ((nca.Header.TitleId & 0xFFFFFFFFFFFFE000).ToString("x16") != _titleId)
                                {
                                    break;
                                }

                                parentIter ??= ((TreeStore)_dlcTreeView.Model).AppendValues(true, "", containerPath);

                                ((TreeStore)_dlcTreeView.Model).AppendValues(parentIter.Value, true, nca.Header.TitleId.ToString("X16"), fileEntry.FullPath);
                                containsDlc = true;
                            }
                        }

                        if (!containsDlc)
                        {
                            GtkDialog.CreateErrorDialog("指定的文件不包含所选Title的DLC!");
                        }
                    }
                }
            }

            fileChooser.Dispose();
        }

        private void RemoveButton_Clicked(object sender, EventArgs args)
        {
            if (_dlcTreeSelection.GetSelected(out ITreeModel treeModel, out TreeIter treeIter))
            {
                if (_dlcTreeView.Model.IterParent(out TreeIter parentIter, treeIter) && _dlcTreeView.Model.IterNChildren(parentIter) <= 1)
                {
                    ((TreeStore)treeModel).Remove(ref parentIter);
                }
                else
                {
                    ((TreeStore)treeModel).Remove(ref treeIter);
                }
            }
        }
        
        private void RemoveAllButton_Clicked(object sender, EventArgs args)
        {
            List<TreeIter> toRemove = new List<TreeIter>();

            if (_dlcTreeView.Model.GetIterFirst(out TreeIter iter))
            {
                do
                {
                    toRemove.Add(iter);
                } 
                while (_dlcTreeView.Model.IterNext(ref iter));
            }

            foreach (TreeIter i in toRemove)
            {
                TreeIter j = i;
                ((TreeStore)_dlcTreeView.Model).Remove(ref j);
            }
        }

        private void SaveButton_Clicked(object sender, EventArgs args)
        {
            _dlcContainerList.Clear();

            if (_dlcTreeView.Model.GetIterFirst(out TreeIter parentIter))
            {
                do
                {
                    if (_dlcTreeView.Model.IterChildren(out TreeIter childIter, parentIter))
                    {
                        DlcContainer dlcContainer = new DlcContainer
                        {
                            Path       = (string)_dlcTreeView.Model.GetValue(parentIter, 2),
                            DlcNcaList = new List<DlcNca>()
                        };

                        do
                        {
                            dlcContainer.DlcNcaList.Add(new DlcNca
                            {
                                Enabled = (bool)_dlcTreeView.Model.GetValue(childIter, 0),
                                TitleId = Convert.ToUInt64(_dlcTreeView.Model.GetValue(childIter, 1).ToString(), 16),
                                Path    = (string)_dlcTreeView.Model.GetValue(childIter, 2)
                            });
                        }
                        while (_dlcTreeView.Model.IterNext(ref childIter));

                        _dlcContainerList.Add(dlcContainer);
                    }
                }
                while (_dlcTreeView.Model.IterNext(ref parentIter));
            }

            using (FileStream dlcJsonStream = File.Create(_dlcJsonPath, 4096, FileOptions.WriteThrough))
            {
                dlcJsonStream.Write(Encoding.UTF8.GetBytes(JsonHelper.Serialize(_dlcContainerList, true)));
            }

            Dispose();
        }

        private void CancelButton_Clicked(object sender, EventArgs args)
        {
            Dispose();
        }
    }
}
