using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;

/// <summary>
/// CM01: in compact mode the settings line up with the main page and leaving them keeps the editor page. The main page
/// has no caption there (its menu bar is the top row); the settings had a 32 px caption, so everything below moved by
/// 8 px on the way in and back. Leaving the settings also loaded the editor page again - the whole document drawn once
/// more over a light page - which a mark left in the page shows: it is still there afterwards.
/// </summary>
internal static partial class Program
{
    // Of the window's top row (a few pixels down, above the title's letters), the share near the given colour.
    private static double TopRowShare(IntPtr window, (int R, int G, int B) colour)
    {
        if (!GetWindowRect(window, out var r)) return 0;
        int w = r.Right - r.Left, near = 0, all = 0;
        using var shot = new System.Drawing.Bitmap(w, 1);
        using (var g = System.Drawing.Graphics.FromImage(shot)) g.CopyFromScreen(r.Left, r.Top + 6, 0, 0, new System.Drawing.Size(w, 1));
        for (var x = 20; x < w - 20; x += 6)
        {
            var p = shot.GetPixel(x, 0);
            all++;
            if (Math.Abs(p.R - colour.R) <= 4 && Math.Abs(p.G - colour.G) <= 4 && Math.Abs(p.B - colour.B) <= 4) near++;
        }
        return all == 0 ? 0 : near * 100.0 / all;
    }

    private static async Task CM01(List<string> notes)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        using var c = await Session("e2e CM01");
        var id = await Open(c, Fixture("cm01.md", "# CM01\n\nText\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var compactBefore = (bool)(await c.Call("test.settings.get", new { windowId, name = "AppCompactMode" }))["value"]!;
        var themeBefore = (string?)(await c.Call("test.settings.get", new { windowId, name = "CustomTheme" }))["value"];
        try
        {
            await c.Call("test.settings.set", new { windowId, name = "AppCompactMode", value = true });
            // As a reader turns it on: in the settings, then back to the main page, which is made in compact mode.
            await TypeInto(c, id, "");
            await CtrlComma();
            await Task.Delay(1500);
            await CtrlComma();
            await Task.Delay(2000);
            await TypeInto(c, id, "");
            await Task.Delay(1000);
            await c.Call("test.editor.eval", new { windowId, script = "window.__cm01 = 'kept'; 'ok'" });
            async Task<(string Page, double Top)> ContentTop()
            {
                var r = await c.Call("test.layout.contentTop", new { windowId });
                return ((string)r["page"]!, (double)r["contentTop"]!);
            }
            var (mainPage, menuBottomTop) = await ContentTop();
            double? menuBottom = mainPage == "main" ? menuBottomTop : null;
            var editorShot = Screenshot("cm01-editor");
            await CtrlComma();
            await Task.Delay(2000);
            var (settingsPage, settingsTopValue) = await ContentTop();
            double? settingsTop = settingsPage == "settings" ? settingsTopValue : null;
            var settingsShot = Screenshot("cm01-settings");
            await CtrlComma();
            await Task.Delay(2000);
            var (afterPage, afterTop) = await ContentTop();
            double? menuBottomAfter = afterPage == "main" ? afterTop : null;
            // Under a custom theme the settings' top row is the page's colour, the caption buttons on it - it was the
            // window's white over a Sepia page. Sepia's page colour (Tools/Themes/generate.py).
            await c.Call("test.settings.set", new { windowId, name = "CustomTheme", value = "sepia" });
            await CtrlComma();
            await Task.Delay(2000);
            var sepiaRow = TopRowShare(window, (0xf4, 0xec, 0xd8));
            var sepiaShot = Screenshot("cm01-sepia-settings");
            await CtrlComma();
            await Task.Delay(2000);
            notes.Add($"settings under Sepia: {sepiaRow:0}% of the top row in the page's colour; screen {sepiaShot}");
            Check(sepiaRow >= 60, $"under Sepia the settings' top row is the page's colour, not the window's ({sepiaRow:0}%)");
            var mark = (string?)(await c.Call("test.editor.eval", new { windowId, script = "String(window.__cm01)" }))["result"];
            notes.Add($"content top in the window: main page (menu bar row bottom) {menuBottom}, the settings' top {settingsTop}, menu bar bottom after {menuBottomAfter}; page mark {mark}; screens {editorShot} {settingsShot}");
            Check(menuBottom != null && settingsTop != null, "the menu bar and the settings were found");
            Check(Math.Abs((menuBottom ?? 0) - (settingsTop ?? -100)) <= 1, $"the settings start where the main page's top row ends ({settingsTop} vs {menuBottom})");
            Check(menuBottomAfter != null && Math.Abs(menuBottomAfter.Value - (menuBottom ?? 0)) <= 1, "back from the settings, the menu bar is where it was");
            Check(mark == "kept", $"leaving the settings kept the editor page (mark {mark})");
            // And the document is still there to type into.
            TypeChar('Q');
            await WaitForPage(c, id, t => t.Contains('Q'), "a letter after the settings");
        }
        finally
        {
            try { await c.Call("test.settings.set", new { windowId, name = "AppCompactMode", value = compactBefore }); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "CustomTheme", value = themeBefore ?? "" }); } catch { }
            try
            {
                for (var i = 0; i < 3 && ((string?)(await Get(c, id))["text"] ?? "").Contains('Q'); i++)
                    await c.Call("document.undo", new { documentId = id, baseRevision = await Revision(c, id), reveal = "document" });
                await c.Call("document.close", new { documentId = id });
            }
            catch (Exception e) { notes.Add("cleanup: " + e.Message.Split('\n')[0]); }
        }
    }
}
