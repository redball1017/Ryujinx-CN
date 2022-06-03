using Avalonia.Controls;
using Avalonia.Threading;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Ui.Controls;
using Ryujinx.Ava.Ui.Windows;
using Ryujinx.HLE;
using Ryujinx.HLE.HOS.Applets;
using Ryujinx.HLE.HOS.Services.Am.AppletOE.ApplicationProxyService.ApplicationProxy.Types;
using Ryujinx.HLE.Ui;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Ui.Applet
{
    internal class AvaHostUiHandler : IHostUiHandler
    {
        private readonly MainWindow _parent;

        public IHostUiTheme HostUiTheme { get; }

        public AvaHostUiHandler(MainWindow parent)
        {
            _parent = parent;

            HostUiTheme = new AvaloniaHostUiTheme(parent);
        }

        public bool DisplayMessageDialog(ControllerAppletUiArgs args)
        {
            string playerCount = args.PlayerCountMin == args.PlayerCountMax
                ? args.PlayerCountMin.ToString()
                : $"{args.PlayerCountMin}-{args.PlayerCountMax}";

            string key = args.PlayerCountMin == args.PlayerCountMax ? "DialogControllerAppletMessage" : "DialogControllerAppletMessagePlayerRange";

            string message = string.Format(LocaleManager.Instance[key],
                                           playerCount,
                                           args.SupportedStyles,
                                           string.Join(", ", args.SupportedPlayers),
                                           args.IsDocked ? LocaleManager.Instance["DialogControllerAppletDockModeSet"] : "");

            return DisplayMessageDialog(LocaleManager.Instance["DialogControllerAppletTitle"], message);
        }

        public bool DisplayMessageDialog(string title, string message)
        {
            // TODO : Show controller applet. Needs settings window to be implemented.
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                ContentDialogHelper.ShowNotAvailableMessage(_parent);
            });

            return true;
        }

        public bool DisplayInputDialog(SoftwareKeyboardUiArgs args, out string userText)
        {
            ManualResetEvent dialogCloseEvent = new(false);

            bool okPressed = false;
            bool error = false;
            string inputText = args.InitialText ?? "";

            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    var response = await SwkbdAppletDialog.ShowInputDialog(_parent, LocaleManager.Instance["SoftwareKeyboard"], args);

                    if (response.Result == UserResult.Ok)
                    {
                        inputText = response.Input;
                        okPressed = true;
                    }
                }
                catch (Exception ex)
                {
                    error = true;
                    ContentDialogHelper.CreateErrorDialog(_parent, string.Format(LocaleManager.Instance["DialogSoftwareKeyboardErrorExceptionMessage"], ex));
                }
                finally
                {
                    dialogCloseEvent.Set();
                }
            });

            dialogCloseEvent.WaitOne();

            userText = error ? null : inputText;

            return error || okPressed;
        }

        public void ExecuteProgram(Switch device, ProgramSpecifyKind kind, ulong value)
        {
            device.Configuration.UserChannelPersistence.ExecuteProgram(kind, value);
            if (_parent.AppHost != null)
            {
                _parent.AppHost.Stop();
            }
        }

        public bool DisplayErrorAppletDialog(string title, string message, string[] buttons)
        {
            ManualResetEvent dialogCloseEvent = new(false);

            bool showDetails = false;

            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    ErrorAppletWindow msgDialog = new(_parent, buttons, message)
                    {
                        Title = title,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen,
                        Width = 400
                    };

                    object response = await msgDialog.Run();

                    if (response != null && buttons.Length > 1 && (int)response != buttons.Length - 1)
                    {
                        showDetails = true;
                    }

                    dialogCloseEvent.Set();

                    msgDialog.Close();
                }
                catch (Exception ex)
                {
                    dialogCloseEvent.Set();
                    ContentDialogHelper.CreateErrorDialog(_parent, string.Format(LocaleManager.Instance["DialogErrorAppletErrorExceptionMessage"], ex));
                }
            });

            dialogCloseEvent.WaitOne();

            return showDetails;
        }

        public IDynamicTextInputHandler CreateDynamicTextInputHandler()
        {
            return new AvaloniaDynamicTextInputHandler(_parent);
        }
    }
}