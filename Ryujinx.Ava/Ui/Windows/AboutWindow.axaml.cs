﻿using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Common.Utilities;
using Ryujinx.Ui.Common.Helper;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Ui.Windows
{
    public class AboutWindow : StyleableWindow
    {
        public AboutWindow()
        {
            if (Program.PreviewerDetached)
            {
                Title = $"Ryujinx {Program.Version} - " + LocaleManager.Instance["MenuBarHelpAbout"];
            }

            Version = Program.Version;

            DataContext = this;

            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif

            _ = DownloadPatronsJson();
        }

        public string Supporters { get; set; }
        public string Version { get; set; }

        public string Developers => string.Format(LocaleManager.Instance["AboutPageDeveloperListMore"], "gdkchan, Ac_K, Thog, rip in peri peri, LDj3SNuD, emmaus, Thealexbarney, Xpl0itR, GoffyDude, »jD«");

        public TextBlock SupportersTextBlock { get; set; }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            SupportersTextBlock = this.FindControl<TextBlock>("SupportersTextBlock");
        }

        private void Button_OnClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                OpenHelper.OpenUrl(button.Tag.ToString());
            }
        }

        private async Task DownloadPatronsJson()
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                Supporters = LocaleManager.Instance["ConnectionError"];

                return;
            }

            HttpClient httpClient = new();

            try
            {
                string patreonJsonString = await httpClient.GetStringAsync("https://patreon.ryujinx.org/");

                Supporters = string.Join(", ", JsonHelper.Deserialize<string[]>(patreonJsonString));
            }
            catch
            {
                Supporters = LocaleManager.Instance["ApiError"];
            }

            await Dispatcher.UIThread.InvokeAsync(() => SupportersTextBlock.Text = Supporters);
        }

        private void AmiiboLabel_OnPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (sender is TextBlock)
            {
                OpenHelper.OpenUrl("https://amiiboapi.com");
            }
        }
    }
}