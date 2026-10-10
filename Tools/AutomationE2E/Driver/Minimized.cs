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

    /// <summary>
    /// MN03: minimized, written to through the API meanwhile, restored the way MN01 restores it (SW_RESTORE, not
    /// activated): the editor's web view gets its size back - left at the minimized window's 144x0, the page counted
    /// itself hidden, got no frames and took no document (the next case's loads waited for ever).
    /// </summary>
    private static async Task MN03(List<string> notes)
    {
        using var c = await Session("e2e MN03");
        var id = await Open(c, Fixture("mn03.md", "# MN03\n\nbefore\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        await Task.Delay(1500);
        var failures = new List<string>();
        for (var round = 0; round < 3; round++)
        {
            var before = (string?)(await c.Call("test.webview.state", new { windowId }))["viewSize"];
            ShowWindow(window, 6 /* SW_MINIMIZE */);
            await Task.Delay(2000);
            var minimized = (string?)(await c.Call("test.webview.state", new { windowId }))["viewSize"];
            await c.Call("document.replace", new { documentId = id, text = $"# MN03\n\nwhile minimized {round}\n", baseRevision = await Revision(c, id) });
            await Task.Delay(2000);
            ShowWindow(window, 9 /* SW_RESTORE */);
            await Task.Delay(2000);
            var after = (string?)(await c.Call("test.webview.state", new { windowId }))["viewSize"];
            notes.Add($"round {round}: web view {before} -> minimized {minimized} -> restored {after}");
            if (after != before) failures.Add($"round {round}: {after} after restoring, {before} before");
            await Activate(window);
        }
        Check(failures.Count == 0, "the editor's web view gets its size back after the window is restored: " + string.Join("; ", failures));
    }

    /// <summary>
    /// MN04: as a person does it - F11 in and out of full screen, the window minimized with its minimize command and brought
    /// back as a taskbar click does (the restore command, then the window active): its content gets the window's size back
    /// and a document opened at once is shown. Restored without being activated (MN03) the content kept the minimized size.
    /// </summary>
    private static async Task MN04(List<string> notes)
    {
        using var c = await Session("e2e MN04");
        var id = await Open(c, Fixture("mn04.md", "# MN04\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        await Activate(window);
        await Task.Delay(1000);
        foreach (var _ in new[] { 1, 2 }) { Send(Key(0x7A, false), Key(0x7A, true)); await Task.Delay(1500); }
        notes.Add("after F11 twice: " + (string?)(await c.Call("test.webview.state", new { windowId }))["viewSize"]);
        var failures = new List<string>();
        for (var round = 0; round < 3; round++)
        {
            PostMessage(window, 0x0112 /* WM_SYSCOMMAND */, new IntPtr(0xF020) /* SC_MINIMIZE */, IntPtr.Zero);
            await Task.Delay(3000);
            PostMessage(window, 0x0112, new IntPtr(0xF120) /* SC_RESTORE */, IntPtr.Zero);
            await Task.Delay(300);
            await Activate(window);
            await Task.Delay(1500);
            var size = (string?)(await c.Call("test.webview.state", new { windowId }))["viewSize"];
            var marker = $"MN04-{round}";
            var b = await Open(c, Fixture($"mn04-{round}.md", $"# {marker}\n"));
            var shown = await Eventually(async () => await PageShows(c, windowId, marker), 5000);
            notes.Add($"round {round}: web view {size}, a document opened shown: {shown}");
            if (!shown || size == "144x0") failures.Add($"round {round}: {size}, shown {shown}");
        }
        Check(failures.Count == 0, "minimized and brought back as a person does, the window works: " + string.Join("; ", failures));
    }

    /// <summary>
    /// MN05: after F11 in and out of full screen, the window minimized, and a Markdown file opened from Explorer meanwhile
    /// (the exe started with the file hands it to the running one, which brings its window back): the window works and
    /// shows the file.
    /// </summary>
    private static async Task MN05(List<string> notes)
    {
        using var c = await Session("e2e MN05");
        var id = await Open(c, Fixture("mn05.md", "# MN05\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        await Activate(window);
        await Task.Delay(1000);
        foreach (var _ in new[] { 1, 2 }) { Send(Key(0x7A, false), Key(0x7A, true)); await Task.Delay(1500); }
        var failures = new List<string>();
        for (var round = 0; round < 3; round++)
        {
            PostMessage(window, 0x0112 /* WM_SYSCOMMAND */, new IntPtr(0xF020) /* SC_MINIMIZE */, IntPtr.Zero);
            await Task.Delay(3000);
            var marker = $"MN05-{round}";
            var file = Fixture($"mn05-{round}.md", $"# {marker}\n");
            // As a double-click in Explorer: the exe with the file, which hands it to the running one.
            using (var p = Process.Start(new ProcessStartInfo(hostExe, $"--automation-test-root \"{testRoot}\" \"{file}\"") { UseShellExecute = true })) p?.WaitForExit(15000);
            var shown = await Eventually(async () => await PageShows(c, windowId, marker), 8000);
            var size = (string?)(await c.Call("test.webview.state", new { windowId }))["viewSize"];
            notes.Add($"round {round}: web view {size}, the file shown: {shown}, window iconic {IsIconic(window)}");
            if (!shown || size == "144x0") failures.Add($"round {round}: {size}, shown {shown}");
            await Activate(window);
        }
        Check(failures.Count == 0, "a file opened while the window was minimized brings it back working: " + string.Join("; ", failures));
    }
}
