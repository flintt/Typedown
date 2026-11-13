using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;

/// <summary>
/// XM07 (the 2026-11-11 menu report; the user, 2026-11-13): the editor's context menu in languages of long words (German,
/// French, Russian, Portuguese) with the shortcut hints: how wide it is, its widest entry, a screenshot of each, against
/// English. The menu is read in a window opened after the language is set (a window's menus are built in the language
/// it had then). A menu wider than half the screen fails.
/// </summary>
internal static partial class Program
{
    private static async Task XM07(List<string> notes)
    {
        var folder = FixtureFolder("xm07");
        var path = Path.Combine(folder, "xm07.md");
        File.WriteAllText(path, "# XM07\n\nA paragraph to right-click, with some words in it.\n");
        using var c = await Session("e2e XM07");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var language = (string?)(await c.Call("test.settings.get", new { windowId, name = "Language" }))["value"] ?? "";
        var screenWidth = GetSystemMetrics(0);
        var widths = new Dictionary<string, double>();
        try
        {
            foreach (var lang in new[] { "en", "de", "fr", "ru", "pt" })
            {
                await c.Call("test.settings.set", new { windowId, name = "Language", value = lang });
                await Task.Delay(800);
                // A file of its own: one already open in a window is shown there, not in a new one.
                var own = Path.Combine(folder, $"xm07-{lang}.md");
                File.WriteAllText(own, "# XM07\n\nA paragraph to right-click, with some words in it.\n");
                var other = (string)(await c.Call("test.window.open", new { path = own }))["windowId"]!;
                var otherWindow = await WindowOf(other, c);
                try
                {
                    await c.Call("window.setView", new { windowId = other, mode = "visual", bounds = new { x = 60, y = 60, width = 1200, height = 800 } });
                    await Task.Delay(1500);
                    var menu = await ContextMenuAt(c, other, otherWindow, "#ag-editor-id > p:nth-of-type(1)");
                    await Task.Delay(400);
                    var items = menu.Values.Where(e => !e.Current.IsOffscreen).ToList();
                    var right = items.Max(e => e.Current.BoundingRectangle.Right);
                    var left = items.Min(e => e.Current.BoundingRectangle.Left);
                    var widest = items.OrderByDescending(e => e.Current.Name.Length).First().Current.Name;
                    widths[lang] = right - left;
                    notes.Add($"{lang}: menu {right - left:0} px wide ({items.Count} entries; longest \"{widest}\") {Screenshot("XM07-" + lang)}");
                }
                finally
                {
                    Send(Key(0x1B, false), Key(0x1B, true));
                    await Task.Delay(300);
                    try { PostMessage(otherWindow, 0x0010, IntPtr.Zero, IntPtr.Zero); } catch { }
                    await Task.Delay(1000);
                }
            }
            var english = widths.TryGetValue("en", out var en) ? en : 0;
            notes.Add("widths against English: " + string.Join(", ", widths.Select(x => $"{x.Key} {x.Value:0} ({(english > 0 ? x.Value / english : 0):0.00}x)")));
            var tooWide = widths.Where(x => x.Value > screenWidth / 2).Select(x => x.Key).ToList();
            Check(widths.Count == 5 && tooWide.Count == 0, $"the context menu is no wider than half the screen ({screenWidth} px) in every language" + (tooWide.Count == 0 ? "" : ": " + string.Join(", ", tooWide)));
        }
        finally
        {
            try { await c.Call("test.settings.set", new { windowId, name = "Language", value = language }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
