using System;
using System.IO;
using System.Threading.Tasks;
using Typedown.Core.Controls;
using Config = Typedown.Core.Config;

namespace Typedown.Core.Utilities
{
    public static class Log
    {
        /// <summary>%LOCALAPPDATA%\Typedown\logs — crash reports are always written here, regardless of network settings.</summary>
        public static string LogFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Config.AppName, "logs");

        public static void WriteLocal(string type, string content)
        {
            try
            {
                Directory.CreateDirectory(LogFolder);
                var file = Path.Combine(LogFolder, $"{DateTime.Now:yyyyMMdd-HHmmss}-{type}.log");
                File.WriteAllText(file, $"Version: {AboutApp.GetAppVersion()}\nSystem: {Environment.OSVersion.VersionString}\nTime: {DateTime.Now:O}\nType: {type}\n\n{content}\n");
            }
            catch
            {
                // logging must never throw
            }
        }

        public static Task Report(string type, string content)
        {
            WriteLocal(type, content);
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
