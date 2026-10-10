using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

/// <summary>
/// TE01: the restored window resizes from its top edge and top corners - asked as the mouse asks: which window is under
/// the point, and what it answers to WM_NCHITTEST there (a window that answers "transparent" passes it to its parent).
/// On Windows 10 the content window is moved up over the window's top row (MainWindow.KeepTopEdgeInClientArea), which
/// may take that row from the resize border.
/// </summary>
internal static partial class Program
{

    private static async Task TE01(List<string> notes)
    {
        using var c = await Session("e2e TE01");
        var id = await Open(c, Fixture("te01.md", "# TE01\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        ShowWindow(window, 9 /* SW_RESTORE */);
        await Activate(window);
        await Task.Delay(800);
        DwmGetWindowAttribute(window, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out RECT r, Marshal.SizeOf<RECT>());
        int Hit(int x, int y, out string where)
        {
            var w = WindowFromPoint(new POINT { X = x, Y = y });
            var lparam = new IntPtr((y << 16) | (x & 0xFFFF));
            var ht = (int)SendMessageW(w, 0x0084 /* WM_NCHITTEST */, IntPtr.Zero, lparam);
            var name = new System.Text.StringBuilder(128); GetClassName(w, name, name.Capacity);
            where = name.ToString();
            for (var i = 0; ht == -1 /* HTTRANSPARENT */ && i < 5 && GetParent(w) != IntPtr.Zero; i++)
            {
                w = GetParent(w);
                ht = (int)SendMessageW(w, 0x0084, IntPtr.Zero, lparam);
                name.Clear(); GetClassName(w, name, name.Capacity); where += " > " + name;
            }
            return ht;
        }
        var failures = new List<string>();
        foreach (var (label, x, y, expected) in new[] {
            ("top edge", (r.Left + r.Right) / 2 - 80, r.Top + 1, 12 /* HTTOP */),
            ("top-left corner", r.Left + 2, r.Top + 1, 13 /* HTTOPLEFT */),
            ("top-right corner", r.Right - 3, r.Top + 1, 14 /* HTTOPRIGHT */) })
        {
            var ht = Hit(x, y, out var where);
            notes.Add($"{label} at {x},{y}: hit {ht} on {where}");
            if (ht != expected) failures.Add($"{label}: {ht} (expected {expected}) on {where}");
        }
        Check(failures.Count == 0, "the top edge and corners resize: " + string.Join("; ", failures));
    }
}
