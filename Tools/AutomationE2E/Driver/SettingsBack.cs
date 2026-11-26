using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// BK01: the settings left by the title bar's back button, three times, a section or two picked in between - each time
/// the back button is there, it goes back to the editor page, and nothing is thrown; then in and out by Ctrl+, twice, and
/// the editor page has no back button after it. On vm104 (1.4.3.25) the settings'
/// own subscription read the DataContext of a page already out of the tree when Back changed the command: an unhandled
/// NullReferenceException each time, and the notice never reached the caption's back button, which went.
/// </summary>
internal static partial class Program
{
    private static async Task BK01(List<string> notes)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        using var c = await Session("e2e BK01");
        var id = await Open(c, Fixture("bk01.md", "# BK01\n\nText\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var compactBefore = (bool)(await c.Call("test.settings.get", new { windowId, name = "AppCompactMode" }))["value"]!;
        var thrownBefore = LogLines("NullReferenceException").Count;
        async Task<string> Page() => (string)(await c.Call("test.layout.contentTop", new { windowId }))["page"]!;
        try
        {
            await c.Call("test.settings.set", new { windowId, name = "AppCompactMode", value = false });
            await TypeInto(c, id, "");
            for (var round = 1; round <= 3; round++)
            {
                await CtrlComma();
                Check(await Eventually(async () => await Page() == "settings", 5000), $"round {round}: Ctrl+, opens the settings");
                // A section or two picked, as a reader looks around: each is a navigation of the settings' own frame.
                await Click(window, await WaitFor(window, "SettingsNavEditor", 5000));
                await Task.Delay(600);
                if (round != 2)
                {
                    await Click(window, await WaitFor(window, "SettingsNavGeneral", 5000));
                    await Task.Delay(600);
                }
                var back = FindIn(window, "BackButton");
                var box = back?.Current.BoundingRectangle;
                notes.Add($"round {round}: the title bar's back button {box}");
                Check(back != null && box!.Value.Width > 0, $"round {round}: the title bar's back button is there in the settings");
                await Click(window, back!);
                Check(await Eventually(async () => await Page() == "main", 5000), $"round {round}: it goes back to the editor page");
                await Task.Delay(500);
                Check(FindIn(window, "BackButton") is not { } left || left.Current.BoundingRectangle.Width == 0, $"round {round}: no back button on the editor page");
            }
            // Left by Ctrl+, (a new navigation, not Back), twice: the editor page has no back button after it - it had
            // one going to the settings again, with one more entry behind it each time (w10 job 766).
            for (var round = 1; round <= 2; round++)
            {
                await CtrlComma();
                Check(await Eventually(async () => await Page() == "settings", 5000), $"Ctrl+, round {round}: opens the settings");
                await Task.Delay(600);
                await CtrlComma();
                Check(await Eventually(async () => await Page() == "main", 5000), $"Ctrl+, round {round}: again, back to the editor page");
                await Task.Delay(500);
                Check(FindIn(window, "BackButton") is not { } stale || stale.Current.BoundingRectangle.Width == 0, $"Ctrl+, round {round}: no back button on the editor page after it");
            }
            await Task.Delay(1500); // the log is written in batches
            var thrown = LogLines("NullReferenceException").Skip(thrownBefore).ToList();
            notes.Add("thrown meanwhile: " + (thrown.Count == 0 ? "nothing" : string.Join(" | ", thrown.Take(3))));
            Check(thrown.Count == 0, $"nothing thrown going in and out of the settings ({thrown.Count} NullReferenceException)");
            TypeChar('Q');
            await WaitForPage(c, id, t => t.Contains('Q'), "a letter after the settings");
        }
        finally
        {
            try { await c.Call("test.settings.set", new { windowId, name = "AppCompactMode", value = compactBefore }); } catch { }
            try { if (await Page() != "main") await c.Call("test.window.navigate", new { windowId, route = "Main" }); } catch { }
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
