﻿using DiscordRPC;
using Ryujinx.Common;
using Ryujinx.Configuration;
using System;

namespace Ryujinx.Modules
{
    static class DiscordIntegrationModule
    {
        private const string Description = "一个普通的，实验性的Nintendo Switch模拟器.";
        private const string CliendId    = "568815339807309834";

        private static DiscordRpcClient _discordClient;
        private static RichPresence     _discordPresenceMain;

        public static void Initialize()
        {
            _discordPresenceMain = new RichPresence
            {
                Assets = new Assets
                {
                    LargeImageKey  = "ryujinx",
                    LargeImageText = Description
                },
                Details    = "主菜单",
                State      = "Idling",
                Timestamps = Timestamps.Now,
                Buttons    = new Button[]
                {
                    new Button()
                    { 
                        Label = "网站", 
                        Url   = "https://ryujinx.org/"
                    }
                }
            };

            ConfigurationState.Instance.EnableDiscordIntegration.Event += Update;
        }

        private static void Update(object sender, ReactiveEventArgs<bool> evnt)
        {
            if (evnt.OldValue != evnt.NewValue)
            {
                // If the integration was active, disable it and unload everything
                if (evnt.OldValue)
                {
                    _discordClient?.Dispose();

                    _discordClient = null;
                }

                // If we need to activate it and the client isn't active, initialize it
                if (evnt.NewValue && _discordClient == null)
                {
                    _discordClient = new DiscordRpcClient(CliendId);

                    _discordClient.Initialize();
                    _discordClient.SetPresence(_discordPresenceMain);
                }
            }
        }

        public static void SwitchToPlayingState(string titleId, string titleName)
        {
            _discordClient?.SetPresence(new RichPresence
            {
                Assets = new Assets
                {
                    LargeImageKey  = "game",
                    LargeImageText = titleName,
                    SmallImageKey  = "ryujinx",
                    SmallImageText = Description,
                },
                Details    = $"正在游玩 {titleName}",
                State      = (titleId == "0000000000000000") ? "Homebrew" : titleId.ToUpper(),
                Timestamps = Timestamps.Now,
                Buttons    = new Button[]
                {
                    new Button()
                    {
                        Label = "Website",
                        Url   = "https://ryujinx.org/"
                    }
                }
            });
        }

        public static void SwitchToMainMenu()
        {
            _discordClient?.SetPresence(_discordPresenceMain);
        }

        public static void Exit()
        {
            _discordClient?.Dispose();
        }
    }
}