using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// TH05: the theme switched the way a reader did it on a real machine - Dark, Black, Mica off and on (the editor page
/// reloads), then Light: the editor follows every switch, the last one too (it stayed dark under a light window).
/// </summary>
internal static partial class Program
{
    private static async Task TH05(List<string> notes)
    {
        using var c = await Session("e2e TH05");
        var id = await Open(c, Fixture("th05.md", "# TH05\n\ntext\n"));
        var windowId = await WindowIdOf(c, id);
        var theme = (await c.Call("settings.get", new { keys = new[] { "appearance.theme" } }))["values"]!["appearance.theme"]!;
        var mica = (await c.Call("test.settings.get", new { windowId, name = "UseMicaEffect" }))["value"];
        var failures = new List<string>();
        async Task<string> PageTheme() => (string?)(await c.Call("test.editor.eval", new { windowId, script = "String(window.actualTheme)" }))["result"] ?? "";
        var window = await WindowOf(windowId, c);
        // The editor's look on screen, not only what the page says: a spot of its background (right of the text, in the
        // middle of the window) must be dark under a dark theme and light under a light one.
        double EditorLuminance()
        {
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            DwmGetWindowAttribute(window, 9, out RECT r, System.Runtime.InteropServices.Marshal.SizeOf<RECT>());
            int x = r.Right - (r.Right - r.Left) / 6, y = (r.Top + r.Bottom) / 2;
            using var b = new System.Drawing.Bitmap(1, 1);
            using (var g = System.Drawing.Graphics.FromImage(b)) g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(1, 1));
            var k = b.GetPixel(0, 0);
            return 0.2126 * k.R + 0.7152 * k.G + 0.0722 * k.B;
        }
        async Task Expect(string step, string expected)
        {
            string got = "";
            var ok = await Eventually(async () => (got = await PageTheme()) == expected, 6000);
            await Activate(window);
            await Task.Delay(800);
            var lum = EditorLuminance();
            var body = (string?)(await c.Call("test.editor.eval", new { windowId, script = "getComputedStyle(document.body).backgroundColor" }))["result"];
            notes.Add($"{step}: editor {got}, background on screen {lum:0}, body {body}");
            if (!ok) failures.Add($"{step}: the editor shows {got}, not {expected}");
            if (expected != "light" ? lum > 100 : lum < 150) failures.Add($"{step}: the editor's background is {lum:0} on screen under {expected}");
        }
        async Task Theme(string id) => await SetSetting(c, "appearance.theme", JObject.FromObject(new { kind = "builtIn", id }));
        try
        {
            await Theme("light"); await Expect("light", "light");
            await Theme("dark"); await Expect("dark", "dark");
            await Theme("black"); await Expect("black", "black");
            await c.Call("test.settings.set", new { windowId, name = "UseMicaEffect", value = false });
            await Task.Delay(2500);
            await c.Call("test.settings.set", new { windowId, name = "UseMicaEffect", value = true });
            await Task.Delay(4000);
            await Expect("black after Mica off and on", "black");
            await Theme("light"); await Expect("light after that", "light");
            await Theme("dark"); await Expect("dark again", "dark");
            await Theme("light"); await Expect("light again", "light");
            Check(failures.Count == 0, string.Join("; ", failures));
        }
        finally
        {
            await c.Call("test.settings.set", new { windowId, name = "UseMicaEffect", value = mica });
            await SetSetting(c, "appearance.theme", theme);
        }
    }
}
