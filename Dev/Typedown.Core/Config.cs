using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using Windows.ApplicationModel;
using Windows.Storage;

namespace System.Runtime.CompilerServices
{
    public static class IsExternalInit { }
}

namespace Typedown.Core
{
    public static class Config
    {
        // Change this label for local test builds; CI stamps its run and commit.
        public const string TestBuild = "test.20260918.1";

        /// <summary>Windows 11 (build 22000+). Uses RtlGetVersion so the compatibility manifest cannot mask the real build.</summary>
        public static int WindowsBuild { get; } = Math.Max(Utilities.PInvoke.GetWindowsBuildNumber(), Environment.OSVersion.Version.Build);

        public static bool IsMicaSupported { get; } = WindowsBuild >= 22000;

        // Set true to restore crash reports and feedback POSTs to typedown.ownbox.cn.
        public static bool AllowOutboundNetwork { get; } = false;

        public static IReadOnlyList<string> WebView2Args { get; } = new List<string>()
        {
            "--disable-web-security",
            "--allow-file-access-from-files",
            "--flag-switches-begin",
            "--enable-features=msOverlayScrollbarWinStyle",
            "--flag-switches-end"
        };

        public static JsonSerializerSettings EditorJsonSerializerSettings = new()
        {
            ContractResolver = new DefaultContractResolver()
            {
                NamingStrategy = new CamelCaseNamingStrategy(true, true)
            },
            MaxDepth = 256
        };

        private static string localFolderPath;

        /// <summary>
        /// Where settings, the database and the remembered state live. Worked out once: an installed build has
        /// no package identity, so asking the platform for its folder throws, and this is called from the
        /// property that names every one of those files — which meant an exception (and a line in the log)
        /// several times a second.
        /// </summary>
        public static string GetLocalFolderPath() => localFolderPath ??= ResolveLocalFolderPath();

        private static string ResolveLocalFolderPath()
        {
            if (IsPackaged)
            {
                try { return ApplicationData.Current.LocalFolder.Path; }
                catch (Exception) { /* fall through to the folder an installed build uses */ }
            }
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppName);
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            return path;
        }

        public static string AppName => "Typedown";

        public static bool IsPackaged { get; private set; }

        static Config()
        {
            try
            {
                IsPackaged = Package.Current != null;
            }
            catch
            {
                IsPackaged = false;
            }
        }
    }
}
