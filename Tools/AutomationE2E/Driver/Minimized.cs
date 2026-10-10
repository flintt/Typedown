using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// MN01: a minimized window's document is written and saved through the automation API (typedownctl, MCP) as when it
/// is shown. The page of a minimized window may be throttled by the web view; a write waits for the page.
/// </summary>
internal static partial class Program
{
    private static async Task MN01(List<string> notes)
    {
        using var c = await Session("e2e MN01");
        var path = Fixture("mn01.md", "# MN01\n\nbefore\n");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        await Task.Delay(1500);
        ShowWindow(window, 6 /* SW_MINIMIZE */);
        await Task.Delay(3000);
        try
        {
            for (var round = 0; round < 3; round++)
            {
                var text = $"# MN01\n\nwritten while minimized {round}\n";
                var watch = Stopwatch.StartNew();
                await c.Call("document.replace", new { documentId = id, text, baseRevision = await Revision(c, id) }).WaitAsync(TimeSpan.FromSeconds(10));
                var replaced = watch.ElapsedMilliseconds;
                await c.Call("document.save", new { documentId = id }).WaitAsync(TimeSpan.FromSeconds(10));
                notes.Add($"round {round}: replace {replaced} ms, saved {watch.ElapsedMilliseconds} ms");
                Check(Disk(path) == text, $"round {round}: the file has the written text");
                await Task.Delay(2000);
            }
        }
        catch (TimeoutException) { throw new CaseFailed("a write or save to the minimized window's document did not finish within 10 s"); }
        finally { ShowWindow(window, 9 /* SW_RESTORE */); }
    }

    /// <summary>
    /// MN02: minimized a while, then restored: a document opened at once is shown within a few seconds and takes a key.
    /// In a full run the editor page of a just-restored window did not answer for 17 s and then loaded itself again.
    /// </summary>
    private static async Task MN02(List<string> notes)
    {
        using var c = await Session("e2e MN02");
        var id = await Open(c, Fixture("mn02-a.md", "# MN02 A\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        await Task.Delay(1500);
        var times = new List<long>();
        for (var round = 0; round < 3; round++)
        {
            ShowWindow(window, 6 /* SW_MINIMIZE */);
            await Task.Delay(8000);
            ShowWindow(window, 9 /* SW_RESTORE */);
            await Activate(window);
            var marker = $"MN02-B-{round}";
            var watch = Stopwatch.StartNew();
            var b = await Open(c, Fixture($"mn02-b{round}.md", $"# {marker}\n"));
            while (!await PageShows(c, windowId, marker) && watch.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(50);
            times.Add(watch.ElapsedMilliseconds);
            notes.Add($"round {round}: shown after {watch.ElapsedMilliseconds} ms");
            if (watch.Elapsed >= TimeSpan.FromSeconds(30))
            {
                // What the page knows of itself while stuck: visible to it or not, and whether a frame still comes.
                async Task<string> Eval(string script) { try { return (await c.Call("test.editor.eval", new { windowId, script }))["result"]?.ToString() ?? "null"; } catch (Exception e) { return "error " + e.Message.Split('\n')[0]; } }
                notes.Add("page: visibilityState " + await Eval("document.visibilityState") + ", hidden " + await Eval("String(document.hidden)") + ", hasFocus " + await Eval("String(document.hasFocus())"));
                await Eval("(window.__mnFrame = 0, requestAnimationFrame(() => window.__mnFrame = 1), 0)");
                await Task.Delay(1000);
                notes.Add("page: a frame came within 1 s: " + await Eval("String(window.__mnFrame)"));
                notes.Add("window: visible " + IsWindowVisible(window) + ", iconic " + IsIconic(window));
            }
            await TypeInto(c, b, "k");
            var typed = await Eventually(async () => ((string?)(await Get(c, b))["text"] ?? "").Contains('k'), 3000);
            notes.Add($"round {round}: a key typed reached it: {typed}");
            Check(watch.Elapsed < TimeSpan.FromSeconds(30) && typed, $"round {round}: the restored window's editor shows the document and takes keys");
        }
        Check(times.Max() < 5000, $"a document opened right after restoring is shown within 5 s ({string.Join(", ", times)} ms)");
    }
}
