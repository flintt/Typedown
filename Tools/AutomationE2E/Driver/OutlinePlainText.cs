using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// OL06: a plain text document has no outline. Reported (2026-11-05): switched from a Markdown tab to a .txt, the outline
/// went on showing the Markdown file's headings. The first report of a plain text load can come before the page counts the
/// load as begun and was dropped, so nothing replaced the old list; and what the page counted in plain text (a line
/// starting with #, a heading inside a ```md block drawn in it) would have been taken for its outline.
/// </summary>
internal static partial class Program
{
    private static async Task OL06(List<string> notes)
    {
        using var c = await Session("e2e OL06");
        var md = await Open(c, Fixture("ol06-headings.md", "# Alpha\n\nText.\n\n## Beta\n\nMore.\n\n## Gamma\n\nEnd.\n"));
        var txt = await Open(c, Fixture("ol06-plain.txt", "# not a heading here\n\nA plain line.\n\n```md\n# Block heading\n```\n\nLast line.\n"));
        var bare = await Open(c, Fixture("ol06-bare.md", "No heading in this one.\n\nJust text.\n"));
        var windowId = await WindowIdOf(c, md);
        async Task<JObject> State() => (JObject)await c.Call("test.outline.state", new { windowId });
        static string Names(JObject s) => string.Join(",", ((JArray)s["rows"]!).Select(r => (string?)r["name"]));
        static int Shown(JObject s) => (int?)s["shown"] ?? -1;
        async Task<string> Expect(string what, Func<JObject, bool> holds)
        {
            JObject? s = null;
            var ok = await Eventually(async () => holds(s = await State()), 8000, 200);
            var seen = $"rows [{Names(s!)}], shown {Shown(s!)}";
            notes.Add($"{what}: {seen}");
            Check(ok, $"{what} ({seen})");
            return seen;
        }
        bool Headings(JObject s) => Names(s) == "Alpha,Beta,Gamma" && Shown(s) == 1;
        // No rows; the outline block folds itself for a plain text without headings (#46), its page then not there (-1).
        bool Empty(JObject s) => ((JArray)s["rows"]!).Count == 0 && Shown(s) <= 0;
        try
        {
            await c.Call("window.setView", new { windowId, mode = "visual", sidePane = new { open = true, page = "outline" } });
            await c.Call("document.focus", new { documentId = md });
            await Expect("the .md shown: its headings", Headings);
            // Two rounds, the second quickly: a report of the Markdown editor still shown while the text came (2152daee)
            // must not fill the plain text's outline either.
            for (var round = 1; round <= 2; round++)
            {
                await c.Call("document.focus", new { documentId = txt });
                await Expect($"round {round}, the .txt shown: no outline (its # line and its ```md block's heading are not headings)", Empty);
                await c.Call("document.focus", new { documentId = md });
                await Expect($"round {round}, back to the .md: its headings again", Headings);
            }
            // Written into the plain text: still none (each edit sends a report).
            await c.Call("document.focus", new { documentId = txt });
            await Expect("the .txt again", Empty);
            await c.Call("document.replace", new { documentId = txt, baseRevision = await Revision(c, txt), text = "# not a heading here\n\nA plain line.\n\n# written later\n", reveal = "document" });
            await Task.Delay(800);
            await Expect("written into the .txt: still no outline", Empty);
            await c.Call("document.save", new { documentId = txt });
            // The pane closed while the .txt was shown, opened on the .md and on the .txt.
            await c.Call("window.setView", new { windowId, sidePane = new { open = false } });
            await c.Call("document.focus", new { documentId = md });
            await c.Call("window.setView", new { windowId, sidePane = new { open = true, page = "outline" } });
            await Expect("the pane opened on the .md: its headings", Headings);
            await c.Call("document.focus", new { documentId = bare });
            await Expect("a .md with no heading: no outline", Empty);
            await c.Call("document.focus", new { documentId = md });
            await Expect("and the .md with headings after it", Headings);
            notes.Add("log: " + string.Join(" | ", LogLines("outline: emptied").TakeLast(2)));
        }
        finally
        {
            foreach (var id in new[] { txt, bare, md })
            {
                try { await c.Call("document.close", new { documentId = id }); } catch { }
            }
        }
    }
}
