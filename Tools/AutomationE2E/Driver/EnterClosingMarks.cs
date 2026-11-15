using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// ED03 (a reader's report, 2026-11-15): "~~&lt;u&gt;下划线回车&lt;/u&gt;~~" clicked at the end of the text shown and Enter
/// pressed took the hidden "&lt;/u&gt;~~" to the next line as text, and both lines lost their format; "**x**" and "~~x~~"
/// kept their closing marks. A real click just right of the last letter shown (the browser puts the caret before the
/// hidden closing marks), a real Enter: the line stays whole and the new line is empty; Ctrl+Z gives the one line back.
/// Then again with a letter typed on the new line. The same with the underline inside bold and around it.
/// </summary>
internal static partial class Program
{
    private static async Task ED03(List<string> notes)
    {
        string[] lines = { "~~<u>下划线回车</u>~~", "**<u>粗体下划线</u>**", "<u>**下划线粗体**</u>" };
        var original = "# ED03\n\n" + string.Join("\n\n", lines) + "\n\nlast\n";
        using var c = await Session("e2e ED03");
        var id = await Open(c, Fixture("ed03.md", original));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        await WaitUntilDrawn(c, windowId, id, "last");
        async Task<string> Text() => ((string?)(await Get(c, id))["text"] ?? "").Replace("\r", "");
        string Show(string s) => s.Replace("\n", "\\n");
        // A real click just right of the last letter shown of `shown` (in the paragraph holding it), as the reader clicks
        // the end of what they see; the caret's offset in the paragraph's Markdown into the notes.
        async Task ClickEndOf(string shown)
        {
            var literal = Newtonsoft.Json.JsonConvert.ToString(shown);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                await Activate(window);
                var at = await PagePointOnScreen(c, windowId, "(() => { const span = [...document.querySelectorAll('#ag-editor-id span.ag-paragraph')].find(s => s.textContent.includes(" + literal + ")); if (!span) return null; const w = document.createTreeWalker(span, NodeFilter.SHOW_TEXT); let last = null, n; while ((n = w.nextNode())) { if (n.length && !n.parentElement.closest('.ag-hide') && n.data.includes(" + literal + ".slice(-1))) last = n; } if (!last) return null; const i = last.data.lastIndexOf(" + literal + ".slice(-1)); const r = document.createRange(); r.setStart(last, i); r.setEnd(last, i + 1); const b = r.getBoundingClientRect(); return { x: Math.round(b.right + 3), y: Math.round(b.top + b.height / 2) } })()");
                await MovePointerTo(at);
                await LeftClick();
                await Task.Delay(400);
                var caret = (string?)(await c.Call("test.editor.eval", new { windowId, script = "(() => { const s = window.__typedownMuya.contentState; const b = s.getBlock(s.cursor.start.key); return document.hasFocus() + ' ' + JSON.stringify(b && b.text) + ' @' + s.cursor.start.offset })()" }))["result"];
                notes.Add($"click at the end of '{shown}': {caret}");
                if (caret != null && caret.StartsWith("true") && caret.Contains(shown)) return;
            }
            throw new CaseFailed($"the click at the end of '{shown}' did not put the caret in its paragraph");
        }
        async Task<string> Await(Func<string, bool> done, int ms = 3000)
        {
            var text = "";
            await Eventually(async () => done(text = await Text()), ms);
            return text;
        }
        // The line as written, the lines around it as they were, one more paragraph (empty or `typed`) after it: an empty
        // paragraph is held as one more blank line pair.
        string After(string line, string typed) => original.Replace(line + "\n\n", line + "\n\n" + typed + "\n\n");
        // Each line tried even when one before failed (that one undone back to the original first), all failures named.
        var failed = new List<string>();
        try
        {
            foreach (var line in lines)
            try
            {
                var shown = line.Replace("~~", "").Replace("**", "").Replace("<u>", "").Replace("</u>", "");
                // Enter at the end of what is shown: the line whole, an empty line after it (only blank lines added).
                await ClickEndOf(shown);
                Chord("Enter");
                await Task.Delay(400);
                var text = await Await(t => t != original);
                notes.Add($"Enter at the end of '{line}': {Show(text)}");
                Check(text == After(line, ""),
                    $"Enter at the end of '{line}' keeps the line whole and adds an empty line (no closing marks moved to it): {Show(text)}");
                Chord("Ctrl+Z");
                text = await Await(t => t == original);
                notes.Add($"then Ctrl+Z: {Show(text)}");
                Check(text == original, $"Ctrl+Z after that Enter gives the one line back: {Show(text)}");
                await Task.Delay(400);

                // Again, a letter typed on the new line: it is a line of its own, after the formatted one.
                await ClickEndOf(shown);
                Chord("Enter");
                await Task.Delay(300);
                TypeText("X");
                var expected = After(line, "X");
                text = await Await(t => t == expected);
                notes.Add($"Enter and X at the end of '{line}': {Show(text)}");
                Check(text == expected, $"Enter and X at the end of '{line}' put the X on a line of its own after the whole formatted text: {Show(text)}");
                for (var i = 0; i < 3 && (text = await Text()) != original; i++)
                {
                    Chord("Ctrl+Z");
                    text = await Await(t => t == original, 1500);
                }
                Check(text == original, $"Ctrl+Z gives '{line}' back as it was: {Show(text)}");
                await Task.Delay(400);
            }
            catch (CaseFailed e)
            {
                failed.Add(e.Message);
                for (var i = 0; i < 4 && await Text() != original; i++) { Chord("Ctrl+Z"); await Task.Delay(500); }
            }
            if (failed.Count > 0) throw new CaseFailed(string.Join(" / ", failed));
        }
        finally
        {
            try { await c.Call("document.save", new { documentId = id }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
