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

        private static readonly System.Collections.Concurrent.ConcurrentQueue<string> pending = new();
        private static int writerRunning;

        /// <summary>
        /// Puts one line into %LOCALAPPDATA%\Typedown\logs\debug.log. The line is queued and written by a
        /// background task: opening and closing the file for every line used to cost minutes of UI-thread time
        /// when something chatty ran — walking a folder of tens of thousands of files, for instance.
        /// </summary>
        public static void Debug(string message)
        {
            pending.Enqueue($"{DateTime.Now:HH:mm:ss.fff} {message}\n");
            if (System.Threading.Interlocked.Exchange(ref writerRunning, 1) == 1) return;
            Task.Run(async () =>
            {
                try
                {
                    Directory.CreateDirectory(LogFolder);
                    var file = Path.Combine(LogFolder, "debug.log");
                    while (true)
                    {
                        var batch = new System.Text.StringBuilder();
                        while (pending.TryDequeue(out var line)) batch.Append(line);
                        if (batch.Length > 0)
                        {
                            try { File.AppendAllText(file, batch.ToString()); } catch { }
                        }
                        await Task.Delay(500);
                        if (pending.IsEmpty)
                        {
                            System.Threading.Interlocked.Exchange(ref writerRunning, 0);
                            // Anything queued between the check and the release is picked up by the next caller.
                            if (pending.IsEmpty) return;
                            if (System.Threading.Interlocked.Exchange(ref writerRunning, 1) == 1) return;
                        }
                    }
                }
                catch
                {
                    System.Threading.Interlocked.Exchange(ref writerRunning, 0);
                }
            });
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
