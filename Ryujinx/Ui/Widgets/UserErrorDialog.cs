using Gtk;
using Ryujinx.Ui.Helper;

namespace Ryujinx.Ui.Widgets
{
    internal class UserErrorDialog : MessageDialog
    {
        private const string SetupGuideUrl        = "https://github.com/Ryujinx/Ryujinx/wiki/Ryujinx-Setup-&-Configuration-Guide";
        private const int    OkResponseId         = 0;
        private const int    SetupGuideResponseId = 1;

        private readonly UserError _userError;

        private UserErrorDialog(UserError error) : base(null, DialogFlags.Modal, MessageType.Error, ButtonsType.None, null)
        {
            _userError = error;

            WindowPosition     = WindowPosition.Center;
            SecondaryUseMarkup = true;

            Response += UserErrorDialog_Response;

            SetSizeRequest(120, 50);

            AddButton("OK", OkResponseId);

            bool isInSetupGuide = IsCoveredBySetupGuide(error);

            if (isInSetupGuide)
            {
                AddButton("打开新手向导", SetupGuideResponseId);
            }

            string errorCode = GetErrorCode(error);

            SecondaryUseMarkup = true;

            Title         = $"Ryujinx 错误 ({errorCode})";
            Text          = $"{errorCode}: {GetErrorTitle(error)}";
            SecondaryText = GetErrorDescription(error);

            if (isInSetupGuide)
            {
                SecondaryText += "\n<b>有关如何修复此错误的更多信息，请遵循我们的设置指南.</b>";
            }
        }

        private string GetErrorCode(UserError error)
        {
            return $"RYU-{(uint)error:X4}";
        }

        private string GetErrorTitle(UserError error)
        {
            return error switch
            {
                UserError.NoKeys                => "找不到密钥",
                UserError.NoFirmware            => "找不到固件",
                UserError.FirmwareParsingFailed => "固件解析错误",
                UserError.ApplicationNotFound   => "找不到应用程序",
                UserError.Unknown               => "未知错误",
                _                               => "Undefined error",
            };
        }

        private string GetErrorDescription(UserError error)
        {
            return error switch
            {
                UserError.NoKeys                => "Ryujinx 无法找到您的“prod.keys”文件",
                UserError.NoFirmware            => "Ryujinx无法找到任何已安装的固件",
                UserError.FirmwareParsingFailed => "Ryujinx 无法解析提供的固件。 这通常是由过期的密钥引起的。",
                UserError.ApplicationNotFound   => "Ryujinx 在给定路径找不到有效的应用程序。.",
                UserError.Unknown               => "发生了一个未知的错误!",
                _                               => "An undefined error occured! This shouldn't happen, please contact a dev!",
            };
        }

        private static bool IsCoveredBySetupGuide(UserError error)
        {
            return error switch
            {
                UserError.NoKeys or
                UserError.NoFirmware or 
                UserError.FirmwareParsingFailed => true,
                _                               => false,
            };
        }

        private static string GetSetupGuideUrl(UserError error)
        {
            if (!IsCoveredBySetupGuide(error))
            {
                return null;
            }

            return error switch
            {
                UserError.NoKeys     => SetupGuideUrl + "#initial-setup---placement-of-prodkeys",
                UserError.NoFirmware => SetupGuideUrl + "#initial-setup-continued---installation-of-firmware",
                _                    => SetupGuideUrl,
            };
        }

        private void UserErrorDialog_Response(object sender, ResponseArgs args)
        {
            int responseId = (int)args.ResponseId;

            if (responseId == SetupGuideResponseId)
            {
                OpenHelper.OpenUrl(GetSetupGuideUrl(_userError));
            }

            Dispose();
        }

        public static void CreateUserErrorDialog(UserError error)
        {
            new UserErrorDialog(error).Run();
        }
    }
}