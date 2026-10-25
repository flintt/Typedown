using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

/// <summary>
/// TH04: with the app's theme following the system, Windows switched between light and dark while the app runs: the
/// caption buttons stay readable (their glyph contrasts with what is under it) and the editor follows. XamlUI's window
/// re-applied the theme on UISettings.ColorValuesChanged; the WinUI 3 window set the caption colours once.
/// </summary>
internal static partial class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeoutW(IntPtr window, uint message, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);

    private static void SetSystemTheme(bool light)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        key.SetValue("AppsUseLightTheme", light ? 1 : 0, RegistryValueKind.DWord);
        SendMessageTimeoutW(new IntPtr(0xFFFF), 0x001A /* WM_SETTINGCHANGE */, IntPtr.Zero, "ImmersiveColorSet", 0x0002 /* SMTO_ABORTIFHUNG */, 3000, out _);
    }

    private static double Luminance(System.Drawing.Color k) => 0.2126 * k.R + 0.7152 * k.G + 0.0722 * k.B;

    private static async Task TH04(List<string> notes)
    {
        using var c = await Session("e2e TH04");
        var id = await Open(c, Fixture("th04.md", "# TH04\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        using var personalize = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var original = personalize?.GetValue("AppsUseLightTheme") as int? ?? 1;
        var theme = (await c.Call("settings.get", new { keys = new[] { "appearance.theme" } }))["values"]!["appearance.theme"]!;
        var failures = new List<string>();
        async Task Measure(string when, bool dark)
        {
            await Activate(window);
            await Task.Delay(2500);
            DwmGetWindowAttribute(window, 9, out RECT r, Marshal.SizeOf<RECT>());
            var scale = GetDpiForWindow(window) / 96.0;
            // The close button: the top-right 46x32 (logical) of the window.
            int w = (int)(46 * scale), h = (int)(30 * scale), x0 = r.Right - w, y0 = r.Top + 1;
            using var bmp = new System.Drawing.Bitmap(w, h);
            using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen(x0, y0, 0, 0, bmp.Size);
            var lum = new List<double>();
            for (var y = 0; y < h; y++) for (var x = 0; x < w; x++) lum.Add(Luminance(bmp.GetPixel(x, y)));
            lum.Sort();
            var background = lum[lum.Count / 2];
            var glyph = dark ? lum[^1] : lum[0]; // a light glyph on dark, a dark glyph on light
            var page = (string?)(await c.Call("test.editor.eval", new { windowId, script = "String(window.actualTheme)" }))["result"] ?? "";
            notes.Add($"{when}: close button background {background:0}, glyph {glyph:0} (contrast {Math.Abs(glyph - background):0}); editor theme {page}");
            if (Math.Abs(glyph - background) < 90) failures.Add($"{when}: the close button's glyph does not stand out ({glyph:0} on {background:0})");
            if (page != (dark ? "dark" : "light")) failures.Add($"{when}: the editor did not follow ({page})");
        }
        try
        {
            await SetSetting(c, "appearance.theme", JObject.FromObject(new { kind = "builtIn", id = "system" }));
            SetSystemTheme(true);
            await Measure("system light", false);
            SetSystemTheme(false);
            await Measure("system switched to dark", true);
            SetSystemTheme(true);
            await Measure("system switched back to light", false);
            Check(failures.Count == 0, string.Join("; ", failures));
        }
        finally
        {
            SetSystemTheme(original != 0);
            await SetSetting(c, "appearance.theme", theme);
        }
    }
}
