using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// BM01: Settings > Editor > \( \) and \[ \] as formula delimiters, on Windows (the user, 2026-11-14: formulas with
/// these delimiters showed as text). Off (the default), only $ and $$ are formulas; turned on with the document shown,
/// \( \) and \[ \] are drawn as formulas and the text the host holds is the file's. A \( \) typed with real keys is a
/// formula, and saved, the file has it and its \[ \] block as written.
/// </summary>
internal static partial class Program
{
    private static async Task BM01(List<string> notes)
    {
        const string text = "# BM01\n\nInline \\(E = mc^2\\) and $a^2$.\n\n\\[\n\\int_0^1 x \\, \\mathrm{d}x\n\\]\n\n$$\nD = D_0\n$$\n\nEnd.\n";
        var path = Fixture("bm01.md", text);
        using var c = await Session("e2e BM01");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        async Task<(int inline, string[] blocks)> Look()
        {
            var r = (await c.Call("test.editor.eval", new
            {
                windowId,
                script = "({ inline: document.querySelectorAll('#ag-editor-id span.ag-math .katex').length, blocks: [...document.querySelectorAll('#ag-editor-id figure[data-role=\"MULTIPLEMATH\"]')].map(f => f.getAttribute('data-mathstyle') || '') })"
            }))["result"]!;
            return ((int)r["inline"]!, r["blocks"]!.Select(b => (string)b!).ToArray());
        }
        try
        {
            await c.Call("test.settings.set", new { windowId, name = "BracketMath", value = false });
            await Eventually(async () => await PageShows(c, windowId, "End."), 20000, 200);
            await Task.Delay(500);
            var off = await Look();
            notes.Add($"off: {off.inline} inline formula(s), blocks [{string.Join(", ", off.blocks)}]");
            Check(off.inline == 1 && off.blocks.Length == 1, $"off, only the $ and $$ formulas ({off.inline} inline, {off.blocks.Length} block(s))");

            await c.Call("test.settings.set", new { windowId, name = "BracketMath", value = true });
            (int inline, string[] blocks) on = default;
            await Eventually(async () => { on = await Look(); return on.inline == 2 && on.blocks.Length == 2; }, 10000, 200);
            notes.Add($"on: {on.inline} inline formula(s), blocks [{string.Join(", ", on.blocks)}]");
            Check(on.inline == 2 && on.blocks.SequenceEqual(new[] { "bracket", "" }), $"turned on with the document shown, \\( \\) and \\[ \\] are formulas too ({on.inline} inline, blocks [{string.Join(", ", on.blocks)}])");
            var held = ((string?)(await Get(c, id))["text"] ?? "").Replace("\r\n", "\n");
            Check(held == text, $"turned on, the text the host holds is the file's ({held.Length} against {text.Length} chars)");

            // Typed with real keys at the end: a formula as soon as it is closed, and saved as typed.
            await TypeIntoWindow(c, windowId, " \\(x^2\\)");
            (int inline, string[] blocks) typed = default;
            await Eventually(async () => { typed = await Look(); return typed.inline == 3; }, 8000, 200);
            notes.Add($"typed: {typed.inline} inline formula(s)");
            Check(typed.inline == 3, $"a \\( \\) typed is drawn as a formula ({typed.inline} inline)");
            await c.Call("document.save", new { documentId = id });
            var saved = Disk(path).Replace("\r\n", "\n");
            notes.Add("saved: " + saved.Replace("\n", "⏎"));
            Check(saved.Contains("End. \\(x^2\\)"), "the file has the \\( \\) as typed");
            Check(saved.Contains("\\[\n\\int_0^1 x \\, \\mathrm{d}x\n\\]"), "the file keeps its \\[ \\] block as written");
        }
        finally
        {
            try { await c.Call("test.settings.set", new { windowId, name = "BracketMath", value = false }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
