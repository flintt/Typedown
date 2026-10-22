using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using Newtonsoft.Json.Linq;

/// <summary>
/// TX01: Settings > Appearance, the theme chosen in its own drop-down - from Dark to Light, and from a dark custom theme
/// (Dracula) to Light - leaves the drop-down drawn: its box and the theme's name are on the page. A reader saw it gone
/// after going from a dark theme to Light; it still took a click, and came back when clicked.
/// </summary>
internal static partial class Program
{
    // Of the box's pixels, the share that differs from the page around it (its top-left corner, just outside it).
    private static double DrawnShare(System.Windows.Rect r, out string shot)
    {
        int x = (int)r.Left, y = (int)r.Top, w = Math.Max(1, (int)r.Width), h = Math.Max(1, (int)r.Height);
        using var bmp = new System.Drawing.Bitmap(w + 8, h + 8);
        using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen(x - 4, y - 4, 0, 0, bmp.Size);
        shot = System.IO.Path.Combine(outputDir, $"tx01-box-{DateTime.Now:HHmmssfff}.png");
        bmp.Save(shot, System.Drawing.Imaging.ImageFormat.Png);
        var page = bmp.GetPixel(1, 1);
        int differ = 0, all = 0;
        for (var px = 4; px < w + 4; px += 2)
            for (var py = 4; py < h + 4; py += 2)
            {
                var p = bmp.GetPixel(px, py);
                all++;
                if (Math.Abs(p.R - page.R) + Math.Abs(p.G - page.G) + Math.Abs(p.B - page.B) > 40) differ++;
            }
        return all == 0 ? 0 : differ * 100.0 / all;
    }

    private static async Task TX01(List<string> notes)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        using var c = await Session("e2e TX01");
        var id = await Open(c, Fixture("tx01.md", "# TX01\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        async Task<JToken> Get(string name) => (await c.Call("test.settings.get", new { windowId, name }))["value"]!;
        var appTheme = await Get("AppTheme");
        var customTheme = await Get("CustomTheme");
        try
        {
            foreach (var (from, custom) in new[] { ("Dark", ""), ("Dracula", "dracula") })
            {
                await c.Call("test.settings.set", new { windowId, name = "CustomTheme", value = custom });
                if (custom == "") await c.Call("test.settings.set", new { windowId, name = "AppTheme", value = 2 });
                await c.Call("test.window.navigate", new { windowId, route = "Settings/View" });
                var box = await WaitFor(window, "ThemeBox");
                await Task.Delay(1200);
                var before = DrawnShare(box.Current.BoundingRectangle, out var beforeShot);
                var dumpBefore = await c.Call("test.element.dump", new { windowId, name = "ThemeBox" });

                // As a reader does it: the drop-down opened with a click, Light (the second item) clicked in its list.
                var (bx, by) = CenterOf(box);
                await Click(window, bx, by);
                await Task.Delay(800);
                var items = Deep.All(AutomationElement.RootElement, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
                    .Cast<AutomationElement>().Where(e => { GetWindowThreadProcessId(window, out var a); return e.Current.ProcessId == (int)a; }).ToList();
                notes.Add($"{from}: items {string.Join(", ", items.Select(i => i.Current.Name))}");
                if (items.Count < 2) throw new CaseFailed("the theme list did not open");
                await ClickInPopup(window, items[1]);
                await Task.Delay(2000);
                box = await WaitFor(window, "ThemeBox");
                var after = DrawnShare(box.Current.BoundingRectangle, out var afterShot);
                var theme = await Get("AppTheme");
                if (after < 3)
                {
                    var dumpAfter = await c.Call("test.element.dump", new { windowId, name = "ThemeBox" });
                    notes.Add($"{from}: before {dumpBefore.ToString(Newtonsoft.Json.Formatting.None)}");
                    notes.Add($"{from}: after {dumpAfter.ToString(Newtonsoft.Json.Formatting.None)}");
                }
                notes.Add($"{from} -> Light (AppTheme {theme}): box drawn {before:0}% before, {after:0}% after; {beforeShot} {afterShot}; screen {Screenshot($"tx01-{from}")}");
                Check(after >= 3, $"from {from} to Light, the theme drop-down is still drawn ({after:0}% of it differs from the page)");
                await c.Call("test.window.navigate", new { windowId, route = "Main" });
                await Task.Delay(800);
            }
        }
        finally
        {
            await c.Call("test.settings.set", new { windowId, name = "CustomTheme", value = customTheme });
            await c.Call("test.settings.set", new { windowId, name = "AppTheme", value = appTheme });
            try { await c.Call("test.window.navigate", new { windowId, route = "Main" }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
