using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    /// <summary>
    /// LU01: Alt+0 (View > Last used tab) after Ctrl+N goes back to the document the new one was made from (a reader's
    /// report, 2026-11-01: now and then it did not). The tab left was kept only by a switch: after A, B and back to A, a
    /// new document's Alt+0 went to B.
    /// </summary>
    private static async Task LU01(List<string> notes)
    {
        using var c = await Session("e2e LU01");
        var a = await Open(c, Fixture("lu01-a.md", "# LU01 A\n"));
        var windowId = await WindowIdOf(c, a);
        var window = await WindowOf(windowId, c);
        var b = await Open(c, Fixture("lu01-b.md", "# LU01 B\n"));
        async Task<string> Active() => (string)((JArray)(await c.Call("window.list"))["windows"]!).First(w => (string)w["windowId"]! == windowId)["activeDocumentId"]!;
        async Task<string> WaitActive(Func<string, bool> done)
        {
            var now = await Active();
            for (var i = 0; i < 40 && !done(now); i++) { await Task.Delay(100); now = await Active(); }
            return now;
        }
        await c.Call("document.focus", new { documentId = a });
        Check(await WaitActive(d => d == a) == a, "A shown again after B");
        await Activate(window);
        await c.Call("test.editor.focus", new { windowId });
        await Task.Delay(300);
        Chord("Ctrl+N");
        var created = await WaitActive(d => d != a);
        Check(created != a && created != b, "Ctrl+N shows a new document");
        await Task.Delay(800);
        Chord("Alt+0");
        var back = await WaitActive(d => d != created);
        notes.Add($"A {a}, B {b}, new {created}; after Alt+0 {back}");
        Check(back == a, $"Alt+0 after Ctrl+N goes back to A, the document the new one was made from ({(back == b ? "B" : back == created ? "stayed on the new one" : back)})");
        Chord("Alt+0");
        var again = await WaitActive(d => d != a);
        Check(again == created, "Alt+0 again goes to the new document");
        try { await c.Call("document.close", new { documentId = created }); } catch (Exception e) { notes.Add("new document not closed: " + e.Message.Split('\n')[0]); }
    }

    /// <summary>
    /// PE01: Settings > Editor > Scroll past the end in the app itself: a long document scrolled all the way down
    /// brings its last line up to about the middle of the editor's area; off, it stays at the bottom (a reader saw no
    /// change with it on, 2026-11-01). EditorBench's check sets the one setting on a bare page; here the host sends
    /// all of them, and the page draws long documents a part at a time.
    /// </summary>
    private static async Task PE01(List<string> notes)
    {
        using var c = await Session("e2e PE01");
        var doc = string.Join("\n\n", Enumerable.Range(1, 80).Select(i => $"Paragraph {i} of a document long enough to scroll.")) + "\n";
        var id = await Open(c, Fixture("pe01.md", doc));
        var windowId = await WindowIdOf(c, id);
        var before = (await c.Call("test.settings.get", new { windowId, name = "ScrollPastEnd" }))["value"]?.ToString();
        notes.Add("ScrollPastEnd before: " + (before ?? "(unset)"));
        const string scroll = "(() => { const s = document.scrollingElement; s.scrollTop = s.scrollHeight; return s.scrollTop })()";
        const string measure = "(() => { const s = document.scrollingElement; const blocks = document.querySelectorAll('#ag-editor-id > *'); const last = blocks[blocks.length - 1].getBoundingClientRect(); const editor = document.getElementById('editor'); return JSON.stringify({ share: last.bottom / innerHeight, scrollTop: s.scrollTop, scrollHeight: s.scrollHeight, innerHeight, padding: editor ? getComputedStyle(editor).paddingBottom : null }) })()";
        var shares = new Dictionary<bool, double>();
        try
        {
            foreach (var on in new[] { false, true })
            {
                await c.Call("test.settings.set", new { windowId, name = "ScrollPastEnd", value = on });
                await Task.Delay(1500);
                // Scrolled twice: a page drawn a part at a time grows as it is scrolled.
                await c.Call("test.editor.eval", new { windowId, script = scroll });
                await Task.Delay(500);
                await c.Call("test.editor.eval", new { windowId, script = scroll });
                await Task.Delay(500);
                var result = (string)(await c.Call("test.editor.eval", new { windowId, script = measure }))["result"]!;
                notes.Add($"{(on ? "on" : "off")}: {result}");
                shares[on] = (double)JObject.Parse(result)["share"]!;
            }
            Check(shares[false] > 0.75, $"off: the last line near the bottom scrolled all the way down ({shares[false] * 100:0}%)");
            Check(shares[true] < 0.65 && shares[false] - shares[true] > 0.25, $"on: the last line comes up to the middle ({shares[true] * 100:0}%)");
        }
        finally
        {
            try { await c.Call("test.settings.set", new { windowId, name = "ScrollPastEnd", value = false }); } catch { }
        }
    }
}
