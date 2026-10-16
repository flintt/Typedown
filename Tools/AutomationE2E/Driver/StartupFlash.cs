using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// SF01: a window opening under the dark theme shows no light frame on its way: from the moment it is on screen, its
/// client area is sampled as often as the screen can be read for two seconds, and no sample may be light. The old host kept a new window hidden
/// until its content had loaded; the WinUI 3 window was shown at once, before its content.
/// </summary>
internal static partial class Program
{
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);

    private static async Task SF01(List<string> notes)
    {
        using var c = await Session("e2e SF01");
        var theme = (await c.Call("settings.get", new { keys = new[] { "appearance.theme" } }))["values"]!["appearance.theme"]!;
        await SetSetting(c, "appearance.theme", JObject.FromObject(new { kind = "builtIn", id = "dark" }));
        await Task.Delay(1500);
        List<IntPtr> HostWindows()
        {
            var found = new List<IntPtr>();
            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out var pid);
                if (pid == hostPid && IsWindowVisible(window) && GetWindowRect(window, out var r) && r.Right - r.Left > 300 && r.Bottom - r.Top > 200)
                    found.Add(window);
                return true;
            }, IntPtr.Zero);
            return found;
        }
        var before = HostWindows();
        string? windowId = null;
        IntPtr shown = IntPtr.Zero;
        try
        {
            var opening = c.Call("test.window.open");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 8000 && shown == IntPtr.Zero)
            {
                shown = HostWindows().FirstOrDefault(w => !before.Contains(w));
                if (shown == IntPtr.Zero) await Task.Delay(5);
            }
            if (shown == IntPtr.Zero) throw new CaseFailed("no new window came on screen");
            var appeared = clock.ElapsedMilliseconds;
            {
                var all = new List<string>();
                EnumWindows((h, _) =>
                {
                    GetWindowThreadProcessId(h, out var pid);
                    if (pid == hostPid && IsWindowVisible(h)) { var t = new System.Text.StringBuilder(200); GetWindowText(h, t, t.Capacity); GetWindowRect(h, out var rr); all.Add($"{h.ToInt64():x}{(h == shown ? "*" : "")}{(before.Contains(h) ? " before" : "")} {rr.Right - rr.Left}x{rr.Bottom - rr.Top} '{t.ToString().Split(" - ")[0]}'"); }
                    return true;
                }, IntPtr.Zero);
                notes.Add("the host's windows when one came up (* taken as the new one): " + string.Join("; ", all));
            }
            // Five points of the client area (the middle and around it), physical pixels on the screen.
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            var samples = new List<(long At, double Light)>();
            // What lay under the middle of the window and what was in front, per sample: the screen is read where the
            // window is, so another window over it would be measured instead.
            var covered = new List<(long At, IntPtr Over, IntPtr Front)>();
            string Title(IntPtr h) { var t = new System.Text.StringBuilder(200); GetWindowText(h, t, t.Capacity); return h == shown ? "the new window" : $"{h.ToInt64():x} '{t}'"; }
            var sampling = System.Diagnostics.Stopwatch.StartNew();
            while (sampling.ElapsedMilliseconds < 2000)
            {
                if (GetWindowRect(shown, out var r))
                {
                    int w = r.Right - r.Left, h = r.Bottom - r.Top;
                    var lightest = 0.0;
                    // The window's area in one read of the screen (a read per point took 30 ms each), the points from it.
                    using (var shot = new System.Drawing.Bitmap(w, h))
                    {
                        using (var g = System.Drawing.Graphics.FromImage(shot)) g.CopyFromScreen(r.Left, r.Top, 0, 0, new System.Drawing.Size(w, h));
                        foreach (var (fx, fy) in new[] { (0.5, 0.5), (0.3, 0.4), (0.7, 0.4), (0.3, 0.7), (0.7, 0.7) })
                        {
                            var k = shot.GetPixel((int)(w * fx), (int)(h * fy));
                            lightest = Math.Max(lightest, 0.2126 * k.R + 0.7152 * k.G + 0.0722 * k.B);
                        }
                    }
                    samples.Add((sampling.ElapsedMilliseconds, lightest));
                    var over = GetAncestor(WindowFromPoint(new POINT { X = (r.Left + r.Right) / 2, Y = (r.Top + r.Bottom) / 2 }), 2);
                    if (over != shown) covered.Add((sampling.ElapsedMilliseconds, over, GetForegroundWindow()));
                }
                await Task.Delay(10);
            }
            windowId = (string?)(await opening)["windowId"];
            // What each window's page was told and paints: the page's colour comes with the host's theme message, its
            // text from the theme's style sheet - a light page with light text means the two disagreed.
            const string themeState = "JSON.stringify({ body: getComputedStyle(document.body).backgroundColor, bodyStyle: document.body.style.backgroundColor, theme: document.documentElement.style.getPropertyValue('--actualTheme'), sheet: (document.getElementById('link_style_editor') || {}).href, text: getComputedStyle(document.body).color })";
            foreach (var w in (JArray)(await c.Call("window.list"))["windows"]!)
            {
                var id = (string)w["windowId"]!;
                try { notes.Add($"page of {(id == windowId ? "the new window" : id)}: {(await c.Call("test.editor.eval", new { windowId = id, script = themeState }))["result"]}"); }
                catch (Exception e) { notes.Add($"page of {id}: {e.Message}"); }
                try
                {
                    var state = await c.Call("test.webview.state", new { windowId = id });
                    notes.Add($"paint of {(id == windowId ? "the new window" : id)}: editor op {state["editorOpacity"]}, web view {state["viewColour"]}; {state["paint"]}");
                }
                catch (Exception e) { notes.Add($"paint of {id}: {e.Message}"); }
            }
            var light = samples.Where(s => s.Light > 150).ToList();
            notes.Add($"on screen {appeared} ms after the call; {samples.Count} samples, lightest {samples.Max(s => s.Light):0}; " +
                string.Join(" ", samples.Where((_, i) => i % 6 == 0).Take(20).Select(s => $"{s.At}:{s.Light:0}")));
            if (covered.Count > 0)
                notes.Add($"the new window was not on top in {covered.Count} samples, from {covered[0].At} ms: over it {Title(covered[0].Over)}, in front {Title(covered[0].Front)}");
            Check(light.Count == 0, $"the dark window showed a light frame {light.Count} times in its first two seconds (first at {light.FirstOrDefault().At} ms, {light.FirstOrDefault().Light:0})");
        }
        finally
        {
            if (shown != IntPtr.Zero) { PostMessage(shown, 0x0010, IntPtr.Zero, IntPtr.Zero); await Task.Delay(1000); }
            await SetSetting(c, "appearance.theme", theme);
        }
    }
}
