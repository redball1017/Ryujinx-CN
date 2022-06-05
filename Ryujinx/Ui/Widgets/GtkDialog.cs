using Gtk;
using Ryujinx.Common.Logging;
using Ryujinx.Ui.Common.Configuration;
using System.Collections.Generic;
using System.Reflection;

namespace Ryujinx.Ui.Widgets
{
    internal class GtkDialog : MessageDialog
    {
        private static bool _isChoiceDialogOpen;

        private GtkDialog(string title, string mainText, string secondaryText, MessageType messageType = MessageType.Other, ButtonsType buttonsType = ButtonsType.Ok) 
            : base(null, DialogFlags.Modal, messageType, buttonsType, null)
        {
            Title              = title;
            Icon               = new Gdk.Pixbuf(Assembly.GetAssembly(typeof(ConfigurationState)), "Ryujinx.Ui.Common.Resources.Logo_Ryujinx.png");
            Text               = mainText;
            SecondaryText      = secondaryText;
            WindowPosition     = WindowPosition.Center;
            SecondaryUseMarkup = true;

            Response += GtkDialog_Response;

            SetSizeRequest(200, 20);
        }

        private void GtkDialog_Response(object sender, ResponseArgs args)
        {
            Dispose();
        }

        internal static void CreateInfoDialog(string mainText, string secondaryText)
        {
            new GtkDialog("Ryujinx - Info", mainText, secondaryText, MessageType.Info).Run();
        }

        internal static void CreateUpdaterInfoDialog(string mainText, string secondaryText)
        {
            new GtkDialog("Ryujinx - Updater", mainText, secondaryText, MessageType.Info).Run();
        }

        internal static MessageDialog CreateWaitingDialog(string mainText, string secondaryText)
        {
            return new GtkDialog("Ryujinx - 等待", mainText, secondaryText, MessageType.Info, ButtonsType.None);
        }

        internal static void CreateWarningDialog(string mainText, string secondaryText)
        {
            new GtkDialog("Ryujinx - 警告", mainText, secondaryText, MessageType.Warning).Run();
        }

        internal static void CreateErrorDialog(string errorMessage)
        {
            Logger.Error?.Print(LogClass.Application, errorMessage);

            new GtkDialog("Ryujinx - 错误", "Ryujinx遇到错误", errorMessage, MessageType.Error).Run();
        }

        internal static MessageDialog CreateConfirmationDialog(string mainText, string secondaryText = "")
        {
            return new GtkDialog("Ryujinx - 确认", mainText, secondaryText, MessageType.Question, ButtonsType.YesNo);
        }

        internal static bool CreateChoiceDialog(string title, string mainText, string secondaryText)
        {
            if (_isChoiceDialogOpen)
            {
                return false;
            }

            _isChoiceDialogOpen = true;

            ResponseType response = (ResponseType)new GtkDialog(title, mainText, secondaryText, MessageType.Question, ButtonsType.YesNo).Run();

            _isChoiceDialogOpen = false;

            return response == ResponseType.Yes;
        }

        internal static ResponseType CreateCustomDialog(string title, string mainText, string secondaryText, Dictionary<int, string> buttons, MessageType messageType = MessageType.Other)
        {
            GtkDialog gtkDialog = new GtkDialog(title, mainText, secondaryText, messageType, ButtonsType.None);

            foreach (var button in buttons)
            {
                gtkDialog.AddButton(button.Value, button.Key);
            }

            return (ResponseType)gtkDialog.Run();
        }

        internal static string CreateInputDialog(Window parent, string title, string mainText, uint inputMax)
        {
            GtkInputDialog gtkDialog    = new GtkInputDialog(parent, title, mainText, inputMax);
            ResponseType   response     = (ResponseType)gtkDialog.Run();
            string         responseText = gtkDialog.InputEntry.Text.TrimEnd();

            gtkDialog.Dispose();

            if (response == ResponseType.Ok)
            {
                return responseText;
            }

            return "";
        }

        internal static bool CreateExitDialog()
        {
            return CreateChoiceDialog("Ryujinx - 退出", "你确定要关闭Ryujinx吗？", "所有未保存的数据将丢失！");
        }
    }
}
