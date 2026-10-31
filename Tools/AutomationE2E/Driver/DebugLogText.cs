using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    // DL01: the debug log holds no document text. A document with a marker word in a heading, a paragraph, a table, a
    // link's text and a code block is opened, left for another tab and come back to (a load with a scroll anchor),
    // shown with its outline and in reading mode, typed into, searched with the find bar, pasted into and exported as
    // HTML; then the test host's debug.log is read for any of the markers. The user's decision (2026-10-27): the log may
    // name files and paths, not what a file says. It said, for one, the anchor's first words with every long load.
    private static readonly string[] Dl01Markers =
    {
        "Zqxheading", "Zqxparagraph", "Zqxtablecell", "Zqxlinktext", "Zqxlinkpath", "Zqxcodeblock", "Zqxtyped", "Zqxpasted", "Zqxsearched",
    };

    private static async Task DL01(List<string> notes)
    {
        using var c = await Session("e2e DL01");
        // Long enough to be drawn as a long document (10,000 characters: LoadDrawn is reported), short of the size the
        // page keeps drawn across a tab switch (50,000): the switch back loads it again, with the anchor.
        var filler = string.Join("\n\n", Enumerable.Range(1, 130).Select(i => $"Filler paragraph number {i} of the document, long enough to make the whole of it several screens tall."));
        var source = "# Zqxheading plan\n\nThe Zqxparagraph opens the text.\n\n| Name | Value |\n| --- | --- |\n| Zqxtablecell | 1 |\n\n"
            + "See [Zqxlinktext here](https://example.com/Zqxlinkpath) for more.\n\n```text\nZqxcodeblock line\n```\n\n## Second part\n\n"
            + filler + "\n\n## Last part\n\nThe end Zqxsearched.\n";
        var path = Fixture("dl01.md", source);
        var other = Fixture("dl01-other.md", "# Other\n\nAnother document.\n");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var drawnBefore = LogLines(": drawn ").Count;
        try
        {
            await WaitUntilDrawn(c, windowId, id, "Zqxheading");
            await c.Call("window.setView", new { windowId, sidePane = new { open = true, page = "outline" } });
            await Task.Delay(1500);

            // Another tab and back: the document is loaded again, at the place it was left (its anchor).
            await c.Call("test.editor.eval", new { windowId, script = "window.scrollTo(0, 0); true" });
            await Task.Delay(800);
            var otherId = await Open(c, other);
            await Task.Delay(2000);
            await Open(c, path);
            await WaitUntilDrawn(c, windowId, id, "Zqxheading");
            await Task.Delay(2000);

            // Reading mode: the outline follows the page as it is scrolled, and says which heading it is on.
            await c.Call("window.setView", new { windowId, mode = "reading" });
            await Task.Delay(1200);
            await c.Call("test.editor.eval", new { windowId, script = "window.scrollTo(0, document.documentElement.scrollHeight); true" });
            await Task.Delay(1500);
            await c.Call("test.editor.eval", new { windowId, script = "window.scrollTo(0, 0); true" });
            await Task.Delay(1500);
            await c.Call("window.setView", new { windowId, mode = "visual" });
            await Task.Delay(1200);

            // Typed into.
            await ClickEditorTextOnce(c, windowId, window, "opens the text");
            await Task.Delay(300);
            Chord("End");
            TypeText(" Zqxtyped");
            await Eventually(async () => ((string)(await Get(c, id))["text"]!).Contains("Zqxtyped"), 5000);

            // Searched with the find bar.
            Chord("Ctrl+F");
            for (var i = 0; i < 40 && FocusedAutomationId() != "TextBoxSearch"; i++) await Task.Delay(50);
            Check(FocusedAutomationId() == "TextBoxSearch", "Ctrl+F gave the find bar's search box the keyboard");
            TypeText("Zqxsearched");
            await Task.Delay(800);
            Chord("Enter");
            await Task.Delay(1500);
            Chord("Esc");
            await Task.Delay(800);

            // Pasted into.
            await c.Call("test.editor.focus", new { windowId });
            await c.Call("test.paste.html", new { windowId, html = "Version:0.9\r\nStartHTML:0\r\nEndHTML:0\r\nStartFragment:0\r\nEndFragment:0\r\n<html><body><!--StartFragment--><p>Zqxpasted words</p><!--EndFragment--></body></html>" });
            var pasted = await Eventually(async () => ((string)(await Get(c, id))["text"]!).Contains("Zqxpasted"), 5000);
            notes.Add("pasted: " + pasted);

            // Exported as HTML.
            var export = Path.Combine(testRoot, "dl01.html");
            if (File.Exists(export)) File.Delete(export);
            await c.Call("test.export.run", new { windowId, type = "html", path = export });
            for (var i = 0; i < 75 && !File.Exists(export); i++) await Task.Delay(200);
            await Task.Delay(500);
            Check(File.Exists(export) && File.ReadAllText(export).Contains("Zqxheading"), "the HTML export was written, with the document in it");

            try { await c.Call("document.close", new { documentId = otherId }); } catch { }
            // The log is written in batches, half a second apart.
            await Task.Delay(2000);
            var drawn = LogLines(": drawn ").Skip(drawnBefore).ToList();
            notes.Add($"long loads drawn: {drawn.Count}; e.g. {drawn.LastOrDefault()?.Trim()}");
            Check(drawn.Count > 0, "a long load was reported (the anchor's line is in the log to be checked)");

            var found = new List<string>();
            foreach (var name in new[] { "debug.old.log", "debug.log" })
            {
                var file = Path.Combine(testRoot, "logs", name);
                if (!File.Exists(file)) continue;
                using var reader = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
                foreach (var line in reader.ReadToEnd().Split('\n'))
                    if (Dl01Markers.Any(m => line.Contains(m, StringComparison.OrdinalIgnoreCase)))
                        found.Add(line.TrimEnd('\r'));
            }
            foreach (var line in found.Take(20)) notes.Add("in the log: " + (line.Length > 300 ? line.Substring(0, 300) + "..." : line));
            var which = Dl01Markers.Where(m => found.Any(l => l.Contains(m, StringComparison.OrdinalIgnoreCase))).ToList();
            Check(found.Count == 0, $"no document text in the debug log: {found.Count} line(s) hold {string.Join(", ", which)}");
        }
        finally
        {
            try { await CloseMenus(); } catch { }
            try { await c.Call("window.setView", new { windowId, mode = "visual", sidePane = new { open = false } }); } catch { }
            try
            {
                await c.Call("document.replace", new { documentId = id, baseRevision = await Revision(c, id), text = source, reveal = "document", normalizationPolicy = "allowUnknown" });
                await c.Call("document.save", new { documentId = id, baseRevision = await Revision(c, id) });
            }
            catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
