using System;
using System.Collections.Generic;
using System.Text;

namespace PZTools.Core.Functions.Update
{
    enum UpdateChannel
    {
        Stable,
        Test,
        Dev
    }

    static class AppUpdater
    {
        public static UpdateChannel GetChannelFromSettings()
        {
            var savedChannel = Config.GetAppSetting<string>("UpdateChannel");
            return Enum.Parse<UpdateChannel>(savedChannel, true);
        }

        public static UpdateChannel UpdateChannel { get; private set; }
        public static void SwitchUpdateChannel(UpdateChannel newChannel)
        {
            UpdateChannel = newChannel; 
        }

        public static Task<bool> CheckForUpdates()
        {
            return Task.FromResult(false);
        }

        public static Task<bool> StartUpdate()
        {
            return Task.FromResult(true);
        }
    }
}
