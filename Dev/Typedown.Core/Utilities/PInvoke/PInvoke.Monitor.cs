using System.Runtime.InteropServices;

namespace Typedown.Core.Utilities
{
    public static partial class PInvoke
    {
        public const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

        /// <summary>System double-click interval in milliseconds.</summary>
        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern uint GetDoubleClickTime();

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern bool GetMonitorInfoW(nint hMonitor, ref MONITORINFO lpmi);

        /// <summary>Full bounds of the monitor the window is (mostly) on.</summary>
        public static RECT GetWindowMonitorRect(nint hwnd)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfoW(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref info);
            return info.rcMonitor;
        }
    }
}
