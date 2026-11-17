using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Typedown.Cli
{
    /// <summary>
    /// A long-running command (mcp) started from an installed package goes when the package is updated. Windows keeps a
    /// package's processes in one container: an MCP server an agent had started from the old version kept the old one's,
    /// and the new version would not start ("another program is using this file", 0x80070020) until it was ended (a
    /// reader, 2026-11-17). The update takes the old version's registration away at once (its folder stays while in use):
    /// gone, the server ends, and the agent's next start of it is the new version.
    /// </summary>
    public static class PackageSuperseded
    {
        private const string Packages = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages\";

        /// <summary>The package's full name when this runs from an installed package's folder (WindowsApps\name\...), else null.</summary>
        public static string? PackageOf(string baseDirectory)
        {
            var dir = new DirectoryInfo(baseDirectory.TrimEnd('\\', '/'));
            for (; dir?.Parent != null; dir = dir.Parent)
                if (string.Equals(dir.Parent.Name, "WindowsApps", StringComparison.OrdinalIgnoreCase) && dir.Name.Contains("__"))
                    return dir.Name;
            return null;
        }

        /// <summary>Whether the package is registered for this user (Windows only).</summary>
        public static bool Registered(string fullName)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return true;
            // advapi32 itself: the oldest framework the tool is built for (netcoreapp3.1) has no Registry class.
            if (RegOpenKeyExW(HKEY_CURRENT_USER, Packages + fullName, 0, KEY_READ, out var key) != 0) return false;
            RegCloseKey(key);
            return true;
        }

        private static readonly IntPtr HKEY_CURRENT_USER = new IntPtr(unchecked((int)0x80000001));
        private const int KEY_READ = 0x20019;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegOpenKeyExW(IntPtr hKey, string subKey, int options, int samDesired, out IntPtr result);

        [DllImport("advapi32.dll")]
        private static extern int RegCloseKey(IntPtr hKey);

        /// <summary>
        /// Calls gone once the package is no longer registered, looking every interval; nothing when not run from a
        /// package, or when its registration is not found to begin with (nothing to tell an update by). The timer, to keep.
        /// </summary>
        public static Timer? Watch(string? package, Func<string, bool> registered, Action gone, TimeSpan interval)
        {
            if (package == null || !registered(package)) return null;
            var done = 0;
            Timer? timer = null;
            timer = new Timer(_ =>
            {
                if (registered(package) || Interlocked.Exchange(ref done, 1) == 1) return;
                timer?.Dispose();
                gone();
            }, null, interval, interval);
            return timer;
        }
    }
}
