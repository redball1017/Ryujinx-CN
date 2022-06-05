﻿using Gtk;
using Ryujinx.Ui.Common.Configuration;
using System.Reflection;

namespace Ryujinx.Ui.Applet
{
    internal class ErrorAppletDialog : MessageDialog
    {
        public ErrorAppletDialog(Window parentWindow, DialogFlags dialogFlags, MessageType messageType, string[] buttons) : base(parentWindow, dialogFlags, messageType, ButtonsType.None, null)
        {
            Icon = new Gdk.Pixbuf(Assembly.GetAssembly(typeof(ConfigurationState)), "Ryujinx.Ui.Common.Resources.Logo_Ryujinx.png");

            int responseId = 0;

            if (buttons != null)
            {
                foreach (string buttonText in buttons)
                {
                    AddButton(buttonText, responseId);
                    responseId++;
                }
            }
            else
            {
                AddButton("好的", 0);
            }
            
            ShowAll();
        }
    }
}