using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// OL03: the outline marks the heading being read as soon as the page is shown again, without the reader scrolling.
/// Reported: the outline showed no current heading until the page was scrolled up or down. The page loaded again (at
/// start-up, or back from the settings) numbers its blocks as before, so it named the same heading the host had marked;
/// the host took that for "marked already" and left the new outline with nothing marked.
/// </summary>
internal static partial class Program
{
    private static async Task OL03(List<string> notes)
    {
        using var c = await Session("e2e OL03");
        var text = new System.Text.StringBuilder();
        for (var a = 1; a <= 30; a++)
        {
            text.Append($"## Mike {a}\n\n");
            for (var p = 0; p < 3; p++) text.Append($"Paragraph {p} of Mike {a}, long enough to take a line or two of the page, so that thirty sections make a document several screens tall.\n\n");
        }
        var id = await Open(c, Fixture("ol03-mike.md", text.ToString()));
        var windowId = await WindowIdOf(c, id);
        async Task<JArray> Rows() => (JArray)(await c.Call("test.outline.state", new { windowId }))["rows"]!;
        async Task<string> Marked()
        {
            var rows = await Rows();
            return string.Join(",", rows.Where(r => (bool?)r["marked"] == true).Select(r => (string?)r["name"])) + " / model " +
                   string.Join(",", rows.Where(r => (bool?)r["selected"] == true).Select(r => (string?)r["name"]));
        }
        // Reading mode: the outline follows the page as it is scrolled.
        await c.Call("window.setView", new { windowId, mode = "reading", sidePane = new { open = true, page = "outline" } });
        await Task.Delay(1500);
        await c.Call("test.editor.eval", new { windowId, script = "document.getElementById('ag-editor-id').querySelectorAll(':scope > h2')[11].scrollIntoView(); window.scrollBy(0, -10); true" });
        string? before = null;
        await Eventually(async () => (before = await Marked()).StartsWith("Mike 12 "), 5000, 200);
        notes.Add("scrolled to Mike 12: " + before);
        Check(before!.StartsWith("Mike 12 "), $"scrolled to it, the outline marks Mike 12 ({before})");
        try
        {
            // The page loaded again, as leaving the settings does (and as at start-up): no scroll after it.
            await c.Call("test.window.navigate", new { windowId, route = "Settings" });
            await Task.Delay(1500);
            await c.Call("test.window.navigate", new { windowId, route = "Main" });
            string? after = null;
            await Eventually(async () => (after = await Marked()).StartsWith("Mike 12 "), 8000, 200);
            notes.Add("back from the settings: " + after + "; log: " + string.Join(" | ", LogLines("outline: ").TakeLast(4)));
            Check(after!.StartsWith("Mike 12 /"), $"back from the settings, without a scroll, the outline marks Mike 12 again ({after})");
        }
        finally
        {
            try { await c.Call("test.window.navigate", new { windowId, route = "Main" }); } catch { }
            await c.Call("window.setView", new { windowId, mode = "visual" });
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
