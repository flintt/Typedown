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
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

    private static async Task FS03(List<string> notes)
    {
        using var c = await Session("e2e FS03");
        var id = await Open(c, Fixture("fs03.md", "# FS03\n\nText\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        async Task<JToken> Layout() => await c.Call("test.window.layout", new { windowId });
        var compactBefore = (bool)(await c.Call("test.settings.get", new { windowId, name = "AppCompactMode" }))["value"]!;
        var failures = new List<string>();
        try
        {
            foreach (var compact in new[] { false, true })
            {
                await c.Call("test.settings.set", new { windowId, name = "AppCompactMode", value = compact });
                // Changed where a person changes it: coming back from the settings page builds the main page anew.
                await c.Call("test.window.navigate", new { windowId, route = "Settings/General" });
                await Task.Delay(1200);
                await c.Call("test.window.navigate", new { windowId, route = "Main" });
                await Task.Delay(1500);
                var failure = await Tries(compact ? "compact" : "title row");
                if (failure != null) failures.Add(failure);
            }
        }
        finally
        {
            await c.Call("test.settings.set", new { windowId, name = "AppCompactMode", value = compactBefore });
            await c.Call("test.window.navigate", new { windowId, route = "Settings/General" });
            await Task.Delay(800);
            await c.Call("test.window.navigate", new { windowId, route = "Main" });
            await Task.Delay(800);
        }
        Check(failures.Count == 0, string.Join("; ", failures));

        // F11, ten tries at the top edge, F11 again: what failed, or null.
        async Task<string?> Tries(string mode)
        {
            await Activate(window);
            Send(Key(0x7A, false), Key(0x7A, true));
            var state = await Layout();
            for (var i = 0; i < 30 && !(bool)state["fullScreen"]!; i++) { await Task.Delay(100); state = await Layout(); }
            await Task.Delay(1000);
            try
            {
                if (!(bool)(await Layout())["fullScreen"]!) return $"{mode}: F11 did not put the window in full screen";
                GetWindowRect(window, out var r);
                notes.Add($"{mode}: full screen at {r.Left},{r.Top} {r.Right - r.Left}x{r.Bottom - r.Top}; {(string?)(await Layout())["nonClientRegions"]}");
                string HitAt(int x, int y)
                {
                    var w = WindowFromPoint(new POINT { X = x, Y = y });
                    var lparam = new IntPtr((y << 16) | (x & 0xFFFF));
                    var ht = (int)SendMessageW(w, 0x0084 /* WM_NCHITTEST */, IntPtr.Zero, lparam);
                    var name = new System.Text.StringBuilder(128); GetClassName(w, name, name.Capacity);
                    return $"{name} ht {ht}";
                }
                // Real pointer input, as a mouse gives it (SetCursorPos moves the cursor without the pointer messages a
                // mouse sends), in absolute coordinates over the primary screen.
                int screenW = GetSystemMetrics(0), screenH = GetSystemMetrics(1);
                void MoveTo(int px, int py) => Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dx = px * 65535 / (screenW - 1), dy = py * 65535 / (screenH - 1), dwFlags = 0x0001 | 0x8000 } } });
                var revealed = 0;
                var hiddenAgain = 0;
                var tries = 10;
                for (var t = 0; t < tries; t++)
                {
                    // Away from the top: the bar hides.
                    var x = r.Left + (r.Right - r.Left) * (2 + t % 6) / 10;
                    MoveTo(x, r.Top + 300);
                    await Task.Delay(900);
                    if (!(bool)(await Layout())["menuBarVisible"]!) hiddenAgain++;
                    // Up to the edge as a hand moves a mouse: a few pixels a step, ending on the top row.
                    for (var y = r.Top + 60; y > r.Top; y -= 6) { MoveTo(x, y); await Task.Delay(15); }
                    MoveTo(x, r.Top);
                    await Task.Delay(700);
                    var layout = await Layout();
                    var shown = (bool)layout["menuBarVisible"]!;
                    if (shown) revealed++;
                    if (t == 0) notes.Add($"{mode}: strip {layout["revealStrip"]}; at the top {layout["hitAtTop"]}; top row {HitAt(x, r.Top)}, row 1 {HitAt(x, r.Top + 1)}");
                    if (t < 3 || !shown) notes.Add($"{mode} try {t} at x={x}: revealed {shown}");
                }
                MoveTo((r.Left + r.Right) / 2, r.Top + 300);
                notes.Add($"{mode}: revealed {revealed} of {tries}, {hiddenAgain} tries started hidden");
                // Each try starts from a hidden bar: one that stays out after the pointer left would make every later try pass.
                if (hiddenAgain != tries) return $"{mode}: the menu bar did not hide when the pointer moved away ({hiddenAgain} of {tries} tries started hidden)";
                if (revealed != tries) return $"{mode}: the menu bar was revealed {revealed} of {tries} times the pointer reached the top edge";
                return null;
            }
            finally
            {
                await Activate(window);
                Send(Key(0x7A, false), Key(0x7A, true));
                await Task.Delay(1000);
            }
        }
    }
}
