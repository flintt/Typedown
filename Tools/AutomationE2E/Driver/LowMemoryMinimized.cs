using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// MM02: Settings > General > Release memory when minimized. Off (the default), a minimized window's web view keeps the
/// normal memory target; on, it gets the low one while minimized - and gives back its caches, the private memory before
/// and after in the notes - and the normal one again once the window is restored, its document still shown.
/// </summary>
internal static partial class Program
{
    private static async Task MM02(List<string> notes)
    {
        var sb = new StringBuilder("# mm02\n\n");
        for (var i = 0; i < 300; i++)
            sb.Append($"## Section {i}\n\nA paragraph with **bold** and a formula $x_{i}^2$, and a note[^n{i}].\n\n$$\n\\sum_{{k=0}}^{{{i}}} k\n$$\n\n[^n{i}]: Footnote {i}.\n\n");
        var path = Fixture("mm02.md", sb.ToString());
        using var c = await Session("e2e MM02");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        async Task<string> Target() => (string?)(await c.Call("test.webview.state", new { windowId }))["memoryTarget"] ?? "?";
        try
        {
            await Eventually(async () => await PageShows(c, windowId, "Section 299"), 60000, 200);
            // Off: nothing changes when the window is minimized.
            await c.Call("test.settings.set", new { windowId, name = "ReleaseMemoryWhenMinimized", value = false });
            ShowWindow(window, 6 /* SW_MINIMIZE */);
            await Task.Delay(2000);
            var offTarget = await Target();
            notes.Add($"off, minimized: memory target {offTarget}");
            ShowWindow(window, 9 /* SW_RESTORE */);
            await Activate(window);
            await Task.Delay(1500);
            Check(offTarget == "Normal", $"off, a minimized window keeps the normal memory target ({offTarget})");

            // On: low while minimized, normal again once restored.
            await c.Call("test.settings.set", new { windowId, name = "ReleaseMemoryWhenMinimized", value = true });
            await Task.Delay(1500);
            var before = TreeMemory(hostPid).privateMb;
            ShowWindow(window, 6 /* SW_MINIMIZE */);
            var lowTarget = "?";
            await Eventually(async () => (lowTarget = await Target()) == "Low", 5000, 200);
            await Task.Delay(8000);
            var minimized = TreeMemory(hostPid).privateMb;
            notes.Add($"on, minimized: memory target {lowTarget}; private {before:F0} MB before, {minimized:F0} MB after 8 s minimized ({minimized - before:+0;-0} MB)");
            Check(lowTarget == "Low", $"on, a minimized window's web view gets the low memory target ({lowTarget})");
            ShowWindow(window, 9 /* SW_RESTORE */);
            await Activate(window);
            var normalTarget = "?";
            await Eventually(async () => (normalTarget = await Target()) == "Normal", 5000, 200);
            notes.Add($"restored: memory target {normalTarget}");
            Check(normalTarget == "Normal", $"restored, the normal memory target again ({normalTarget})");
            var shows = false;
            await Eventually(async () => shows = await PageShows(c, windowId, "Section 299"), 15000, 200);
            Check(shows, "restored, the document is still shown");
        }
        finally
        {
            try { await c.Call("test.settings.set", new { windowId, name = "ReleaseMemoryWhenMinimized", value = false }); } catch { }
            ShowWindow(window, 9 /* SW_RESTORE */);
            try { await Activate(window); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
