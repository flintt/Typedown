using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// SA02: Save As onto a file open in another tab is refused (the 2026-11-09 audit's 12): two documents with one path
/// wrote over each other and shared one crash backup. A and B open; from B, Save As with A's path: a dialog says so, A's
/// file on disk keeps its text, B keeps its own path and text, and the window still holds the two documents.
/// </summary>
internal static partial class Program
{
    private static async Task SA02(List<string> notes)
    {
        var folder = FixtureFolder("sa02");
        var a = Path.Combine(folder, "sa02-a.md");
        var b = Path.Combine(folder, "sa02-b.md");
        File.WriteAllText(a, "# sa02 A\n\nA's own text.\n");
        File.WriteAllText(b, "# sa02 B\n\nB's own text.\n");
        using var c = await Session("e2e SA02");
        var idA = await Open(c, a);
        var idB = await Open(c, b);
        var windowId = await WindowIdOf(c, idB);
        try
        {
            await c.Call("document.focus", new { documentId = idB });
            await Task.Delay(500);
            // The save waits for the dialog: asked for, the dialog closed, then its answer read.
            var saving = c.Call("test.file.saveAs", new { windowId, path = a });
            var (title, content) = await PressDialogButton(c, windowId, DialogButton.Close, 8000);
            notes.Add($"dialog: {title} / {content}");
            var result = await saving;
            Check(result["path"] == null || result["path"]!.Type == JTokenType.Null, $"nothing saved ({result["path"]})");
            Check(!string.IsNullOrEmpty(content), "a dialog says why");
            Check(File.ReadAllText(a) == "# sa02 A\n\nA's own text.\n", "A's file keeps its text");
            var docs = (JArray)(await c.Call("document.list", new { windowId }))["documents"]!;
            var paths = docs.Select(d => (string?)d["path"]).ToList();
            notes.Add("documents: " + string.Join(" | ", paths));
            Check(paths.Count(p => string.Equals(p, a, StringComparison.OrdinalIgnoreCase)) == 1, "A is open once");
            Check(paths.Any(p => string.Equals(p, b, StringComparison.OrdinalIgnoreCase)), "B keeps its own path");
        }
        finally
        {
            foreach (var d in new[] { idA, idB }) try { await c.Call("document.close", new { documentId = d }); } catch { }
        }
    }
}
