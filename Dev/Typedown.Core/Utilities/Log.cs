using System;
using System.Threading.Tasks;
using Typedown.Core.Controls;
using Config = Typedown.Core.Config;

namespace Typedown.Core.Utilities
{
    public static class Log
    {
        public static Task Report(string type, string content)
        {
            if (!Config.AllowOutboundNetwork)
                return Task.CompletedTask;
            return Task.Run(() => Common.Post("https://typedown.ownbox.cn/report", new
            {
                version = AboutApp.GetAppVersion(),
                system = Environment.OSVersion.VersionString,
                type,
                content,
            }));
        }
    }
}
