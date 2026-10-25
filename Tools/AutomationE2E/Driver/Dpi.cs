using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

/// <summary>
/// DP01: the window is per-monitor DPI aware. Not aware, Windows draws it at 100% and stretches the picture on a
/// scaled screen - every letter blurred. The XAML Islands host was aware through the manifest XamlUI merged into the
/// app; the WinUI 3 host lost it with XamlUI. Asked of Windows for the window itself, so a 100% screen shows it too.
/// </summary>
internal static partial class Program
{
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern bool AreDpiAwarenessContextsEqual(IntPtr a, IntPtr b);

    private static async Task DP01(List<string> notes)
    {
        using var c = await Session("e2e DP01");
        var id = await Open(c, Fixture("dp01.md", "# DP01\n"));
        var window = await WindowOf(await WindowIdOf(c, id), c);
        var context = GetWindowDpiAwarenessContext(window);
        var awareness = GetAwarenessFromDpiAwarenessContext(context); // 0 unaware, 1 system, 2 per monitor
        var v2 = AreDpiAwarenessContextsEqual(context, new IntPtr(-4)); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        notes.Add($"awareness {awareness}, per-monitor v2 {v2}");
        Check(awareness == 2 && v2, $"the window is per-monitor (v2) DPI aware (awareness {awareness}, v2 {v2})");
    }
}
