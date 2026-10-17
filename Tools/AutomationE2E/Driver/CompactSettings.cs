using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;

/// <summary>
/// CM01: in compact mode the settings have no title bar of their own, as the main page has none: they start at the
/// window's top, in a custom theme's colours there too, the window is dragged by the empty part of their header and
/// their way back is the back button in their pane, the page's title below a strip along the top that drags the window - which goes back to the editor page as it was (a mark left in it
/// is still there: leaving the settings once loaded the page again, the whole document drawn once more). A reader saw
/// a caption in the window's white over a Sepia page, unlike the editor view.
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
        // Where the window was: the drags below move it, and the cases after this one click at points in it.
        GetWindowRect(window, out var placed);
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
            // Under a custom theme: the top row is the page's colour - Sepia's (Tools/Themes/generate.py).
            await c.Call("test.settings.set", new { windowId, name = "CustomTheme", value = "sepia" });
            await CtrlComma();
            await Task.Delay(2000);
            var (settingsPage, settingsTopValue) = await ContentTop();
            double? settingsTop = settingsPage == "settings" ? settingsTopValue : null;
            var sepiaRow = TopRowShare(window, (0xf4, 0xec, 0xd8));
            var settingsShot = Screenshot("cm01-settings");
            notes.Add($"content top in the window: main page (menu bar row bottom) {menuBottom}, the settings' top {settingsTop}; under Sepia {sepiaRow:0}% of the top row in the page's colour; screens {editorShot} {settingsShot}");
            Check(settingsTop != null && settingsTop <= 1, $"the settings start at the window's top, no title bar above them ({settingsTop})");
            Check(sepiaRow >= 60, $"under Sepia the settings' top row is the page's colour, not the window's ({sepiaRow:0}%)");

            // The page's title below the top strip - not against the window's top - and level with the pane's items, the
            // back button above them.
            var back = await WaitFor(window, "NavigationViewBackButton", 5000);
            var title = await WaitFor(window, "BreadcrumbBar", 5000);
            var backBottom = back.Current.BoundingRectangle.Bottom;
            var titleTop = title.Current.BoundingRectangle.Top;
            notes.Add($"back button bottom {backBottom}, the page's title top {titleTop}");
            Check(titleTop >= backBottom - 2, $"the page's title is below the top strip ({titleTop} vs the back button's bottom {backBottom})");

            // The whole top strip drags the window: over the pane, right of the back button, and over the page.
            int screenW = GetSystemMetrics(0), screenH = GetSystemMetrics(1);
            INPUT MoveTo(int x, int y) => new() { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dx = x * 65535 / (screenW - 1), dy = y * 65535 / (screenH - 1), dwFlags = 0x0001 | 0x8000 } } };
            foreach (var (name, share) in new[] { ("over the pane", 0.12), ("over the page", 0.6) })
            {
                GetWindowRect(window, out var before);
                int px = before.Left + (int)((before.Right - before.Left) * share), py = before.Top + 20;
                await Activate(window);
                Send(MoveTo(px, py));
                await Task.Delay(150);
                Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = 0x0002 } } });
                for (var step = 1; step <= 8; step++) { await Task.Delay(30); Send(MoveTo(px + step * 10, py + step * 5)); }
                await Task.Delay(150);
                Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = 0x0004 } } });
                await Task.Delay(500);
                GetWindowRect(window, out var after);
                notes.Add($"dragged {name} from ({px}, {py}) by (80, 40): the window moved by ({after.Left - before.Left}, {after.Top - before.Top})");
                Check(Math.Abs(after.Left - before.Left - 80) <= 8 && Math.Abs(after.Top - before.Top - 40) <= 8, $"the top strip {name} drags the window ({after.Left - before.Left}, {after.Top - before.Top})");
            }

            // Back by the pane's back button.
            await Click(window, await WaitFor(window, "NavigationViewBackButton", 5000));
            await Task.Delay(2000);
            var (afterPage, afterTop) = await ContentTop();
            double? menuBottomAfter = afterPage == "main" ? afterTop : null;
            var mark = (string?)(await c.Call("test.editor.eval", new { windowId, script = "String(window.__cm01)" }))["result"];
            notes.Add($"back: page {afterPage}, menu bar bottom {menuBottomAfter}; page mark {mark}");
            Check(menuBottom != null, "the main page's menu bar was found");
            Check(menuBottomAfter != null && Math.Abs(menuBottomAfter.Value - (menuBottom ?? 0)) <= 1, "the back button goes back to the main page, its menu bar where it was");
            Check(mark == "kept", $"leaving the settings kept the editor page (mark {mark})");
            // And the document is still there to type into.
            TypeChar('Q');
            await WaitForPage(c, id, t => t.Contains('Q'), "a letter after the settings");
        }
        finally
        {
            try { await c.Call("test.settings.set", new { windowId, name = "AppCompactMode", value = compactBefore }); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "CustomTheme", value = themeBefore ?? "" }); } catch { }
            MbSetWindowPos(window, IntPtr.Zero, placed.Left, placed.Top, 0, 0, 0x0001 | 0x0004 | 0x0010); // SWP_NOSIZE | NOZORDER | NOACTIVATE
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
