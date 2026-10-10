using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
}
