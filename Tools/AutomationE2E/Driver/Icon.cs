using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

/// <summary>
/// TI01: the window carries the app's icon (Alt+Tab, the taskbar, the title bar's system menu): asked of the window
/// (WM_GETICON) and of its class. XamlUI registered its window class with the exe's icon; a WinUI 3 window has none
/// unless it is given one.
/// </summary>
internal static partial class Program
{
    [DllImport("user32.dll")] private static extern IntPtr SendMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] private static extern IntPtr GetClassLongPtr(IntPtr window, int index);

    private static async Task TI01(List<string> notes)
    {
        using var c = await Session("e2e TI01");
        var id = await Open(c, Fixture("ti01.md", "# TI01\n"));
        var window = await WindowOf(await WindowIdOf(c, id), c);
        var big = SendMessageW(window, 0x007F /* WM_GETICON */, new IntPtr(1) /* ICON_BIG */, IntPtr.Zero);
        var small = SendMessageW(window, 0x007F, new IntPtr(0) /* ICON_SMALL */, IntPtr.Zero);
        var classIcon = GetClassLongPtr(window, -14 /* GCLP_HICON */);
        var classSmall = GetClassLongPtr(window, -34 /* GCLP_HICONSM */);
        notes.Add($"WM_GETICON big {big}, small {small}; class icon {classIcon}, small {classSmall}");
        Check(big != IntPtr.Zero || classIcon != IntPtr.Zero, "the window has an icon (its own or its class's)");
    }
}
