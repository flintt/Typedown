using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// TC03: a 1M document's tab closed while it is drawn and another file opened right after: the file opened is the one shown, and stays
/// so. The strip asks to show the closed tab's neighbour as the tab goes; that switch waits for the page's text first,
/// and came out of its wait after the file had been opened and shown, putting the neighbour back over it (HJ02/HJ03 on
/// w10: the document opened was never on screen).
/// </summary>
internal static partial class Program
{
    private static async Task TC03(List<string> notes)
    {
        using var c = await Session("e2e TC03");
        string Mixed()
        {
            using var stream = typeof(Program).Assembly.GetManifestResourceStream("hj/mixed-1m.md.gz") ?? throw new CaseFailed("no resource hj/mixed-1m.md.gz");
            using var gz = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
            using var reader = new StreamReader(gz, new System.Text.UTF8Encoding(false));
            return reader.ReadToEnd();
        }
        var text = Mixed();
        var neighbour = await Open(c, Fixture("tc03-neighbour.md", "# TC03 neighbour\n\nA short document beside the long one.\n"));
        var windowId = await WindowIdOf(c, neighbour);
        var bad = new List<string>();
        var opened = new List<string> { neighbour };
        try
        {
            for (var round = 0; round < 5; round++)
            {
                var longId = await Open(c, Fixture($"tc03-long-{round}.md", text));
                // Still being drawn, at different points (on w10 a 1M document takes a few seconds): the page is slow to
                // give its text then, and the strip's switch waits longest.
                await Task.Delay(new[] { 800, 1500, 2500, 400, 1200 }[round]);
                var next = Fixture($"tc03-next-{round}.md", $"# TC03 next {round}\n\nThe file opened right after the long one was closed.\n");
                // Closed, and the next file opened at once (not waiting for anything the close set going).
                await c.Call("document.close", new { documentId = longId });
                var nextId = await Open(c, next);
                opened.Add(nextId);
                await Task.Delay(3000);
                var active = (string?)((JArray)(await c.Call("window.list"))["windows"]!)[0]!["activeDocumentId"];
                var page = (string?)(await c.Call("test.editor.eval", new { windowId, script = "document.getElementById('ag-editor-id')?.textContent.slice(0, 40)" }))["result"] ?? "";
                notes.Add($"round {round}: active {(active == nextId ? "the file opened" : active == neighbour ? "the neighbour" : active)}, page \"{page}\"");
                if (active != nextId || !page.Contains($"TC03 next {round}")) bad.Add($"round {round}: shown {(active == neighbour ? "the neighbour" : active)} / \"{page}\"");
                try { await c.Call("document.close", new { documentId = nextId }); } catch { }
                opened.Remove(nextId);
            }
        }
        finally
        {
            foreach (var id in opened) try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
        Check(bad.Count == 0, $"a file opened right after a long document's tab was closed is the one shown, every round ({string.Join("; ", bad)})");
    }
}
