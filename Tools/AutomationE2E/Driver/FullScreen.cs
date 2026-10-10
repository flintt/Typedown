using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// FS03: in full screen, the pointer moved up to the top edge of the screen as a person moves it reveals the menu bar,
/// every time; moved away, the bar hides again. A reader found it revealed only now and then. Each try notes what the
/// top edge is to Windows there (the window under it and its hit test), and the window's non-client regions.
/// </summary>
internal static partial class Program
{
    private static async Task FS03(List<string> notes)
    {
        using var c = await Session("e2e FS03");
        var id = await Open(c, Fixture("fs03.md", "# FS03\n\nText\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        await Activate(window);
        async Task<JToken> Layout() => await c.Call("test.window.layout", new { windowId });
        Send(Key(0x7A, false), Key(0x7A, true));
        var state = await Layout();
        for (var i = 0; i < 30 && !(bool)state["fullScreen"]!; i++) { await Task.Delay(100); state = await Layout(); }
        await Task.Delay(1000);
        try
        {
            Check((bool)(await Layout())["fullScreen"]!, "F11 put the window in full screen");
            GetWindowRect(window, out var r);
            notes.Add($"full screen at {r.Left},{r.Top} {r.Right - r.Left}x{r.Bottom - r.Top}; {(string?)(await Layout())["nonClientRegions"]}");
            string HitAt(int x, int y)
            {
                var w = WindowFromPoint(new POINT { X = x, Y = y });
                var lparam = new IntPtr((y << 16) | (x & 0xFFFF));
                var ht = (int)SendMessageW(w, 0x0084 /* WM_NCHITTEST */, IntPtr.Zero, lparam);
                var name = new System.Text.StringBuilder(128); GetClassName(w, name, name.Capacity);
                return $"{name} ht {ht}";
            }
            var revealed = 0;
            var tries = 10;
            for (var t = 0; t < tries; t++)
            {
                // Away from the top: the bar hides.
                var x = r.Left + (r.Right - r.Left) * (2 + t % 6) / 10;
                SetCursorPos(x, r.Top + 300);
                await Task.Delay(900);
                var hidden = !(bool)(await Layout())["menuBarVisible"]!;
                // Up to the edge as a hand moves a mouse: a few pixels a step, ending on the top row.
                for (var y = r.Top + 60; y > r.Top; y -= 6) { SetCursorPos(x, y); await Task.Delay(15); }
                SetCursorPos(x, r.Top);
                await Task.Delay(700);
                var shown = (bool)(await Layout())["menuBarVisible"]!;
                if (shown) revealed++;
                notes.Add($"try {t} at x={x}: hidden before {hidden}, revealed {shown}; top row: {HitAt(x, r.Top)}, row 1: {HitAt(x, r.Top + 1)}");
            }
            SetCursorPos((r.Left + r.Right) / 2, r.Top + 300);
            Check(revealed == tries, $"the menu bar is revealed every time the pointer reaches the top edge ({revealed} of {tries})");
        }
        finally
        {
            await Activate(window);
            Send(Key(0x7A, false), Key(0x7A, true));
            await Task.Delay(1000);
        }
    }
}
