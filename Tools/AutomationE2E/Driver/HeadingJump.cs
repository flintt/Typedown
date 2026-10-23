using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// HJ03: the outline's jump to a heading far down while a long document is still drawn, and once more once it is: the
/// heading ends within 50 px of its place. Reported: on a 300k document the jump landed, then about half a second later
/// one move threw the page 88k px off - the load's own delayed scroll back to its caret, run late behind the drawing the
/// jump forces. The page's half is Tools/EditorBench/heading-jump-page.js (heading-jump-check.js in headless Chromium).
/// </summary>
internal static partial class Program
{
    private static async Task HJ03(List<string> notes)
    {
        using var c = await Session("e2e HJ03");
        var small = await Open(c, Fixture("hj03-small.md", "# HJ03\n\nA short document.\n"));
        var windowId = await WindowIdOf(c, small);
        async Task<string> Eval(string script) => (string?)(await c.Call("test.editor.eval", new { windowId, script }))["result"] ?? "";
        string Resource(string name, bool gz)
        {
            using var stream = typeof(Program).Assembly.GetManifestResourceStream("hj/" + name) ?? throw new CaseFailed("no resource hj/" + name);
            using var input = gz ? (Stream)new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress) : stream;
            using var reader = new StreamReader(input, new UTF8Encoding(false));
            return reader.ReadToEnd();
        }
        var page = Resource("heading-jump-page.js", false);
        async Task<JObject> Jump(string tag, double at)
        {
            await Eval(page);
            var started = JObject.Parse(await Eval($"JSON.stringify(window.__hjJump({at.ToString(System.Globalization.CultureInfo.InvariantCulture)}))"));
            if (!(bool)started["ok"]!) return started;
            // The heading chosen (after the load has placed its caret): the host makes the jump, as an outline click does.
            string slug = "";
            if (!await Eventually(async () => (slug = await Eval("window.__hjj && window.__hjj.hostJumps ? window.__hjj.slug : ''")) != "", 30000, 20)) throw new CaseFailed($"{tag}: no heading chosen within 30 s ({await Eval("JSON.stringify(window.__hjj || null)")})");
            await c.Call("test.outline.jump", new { windowId, slug });
            if (!await Eventually(async () => await Eval("String(!!(window.__hjj && window.__hjj.done))") == "true", 60000, 200)) throw new CaseFailed($"{tag}: the jump was not watched to its end within 60 s");
            return JObject.Parse(await Eval("JSON.stringify(window.__hjSummary())"));
        }
        var results = new List<(string Tag, JObject R)>();
        var opened = new List<string>();
        try
        {
            foreach (var (label, file) in new[] { ("100k", "mixed-100k.md.gz"), ("300k", "mixed-300k.md.gz") })
            {
                var id = await Open(c, Fixture($"hj03-{label}.md", Resource(file, true)));
                opened.Add(id);
                // As soon as its first part is there: a heading three quarters down, not drawn yet.
                await Eventually(async () => await Eval("String(!!(window.__typedownMuya && window.__typedownMuya.contentState.blocks.length > 50 && document.getElementById('ag-editor-id')?.children.length > 0))") == "true", 30000, 20);
                var drawing = await Jump($"{label} while drawn", 0.75);
                if (!await Eventually(async () => await Eval("String(!!window.__typedownMuya && !window.__typedownMuya.renderPending)") == "true", 60000, 200)) throw new CaseFailed($"{label}: not drawn within 60 s");
                var drawn = await Jump($"{label} once drawn", 0.45);
                results.Add(($"{label} while drawn", drawing));
                results.Add(($"{label} once drawn", drawn));
                notes.Add($"{label} while drawn: {drawing.ToString(Newtonsoft.Json.Formatting.None)}");
                notes.Add($"{label} once drawn: {drawn.ToString(Newtonsoft.Json.Formatting.None)}");
                try { await c.Call("document.close", new { documentId = id }); } catch { }
                opened.Remove(id);
            }
        }
        finally
        {
            foreach (var id in opened) try { await c.Call("document.close", new { documentId = id }); } catch { }
            try { await c.Call("document.close", new { documentId = small }); } catch { }
        }
        foreach (var (tag, r) in results)
        {
            Check((bool)r["ok"]!, $"{tag}: the jump happened ({r["why"]})");
            if ((bool)r["ok"]!) Check((double)r["endOff"]! <= 50, $"{tag}: the heading ends within 50 px of its place ({r["endOff"]} px, {r["moves"]} moves)");
        }
    }
}
