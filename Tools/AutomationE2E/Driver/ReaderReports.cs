using System;
using System.Collections.Generic;
using System.IO;
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
    /// PT01: a plain text file (.txt) is shown as it is - no Markdown parsed or highlighted (a reader's request,
    /// 2026-11-02): in visual mode laid out as prose (no line numbers), in source mode as code (line numbers), neither
    /// parsing it; the mode switch works on it as on any document. A key typed and saved changes that key alone: its
    /// "#", "**", table, trailing spaces and blank lines are written back as they were. A Markdown tab beside it is in
    /// the visual editor, and back on the .txt it is plain again.
    /// </summary>
    private static async Task PT01(List<string> notes)
    {
        using var c = await Session("e2e PT01");
        var text = "# not a heading\n\n**not bold** and *not italic*   \n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n- not\n- a list\n\n\n\nlast line";
        var path = Path.Combine(fixtures, "pt01.txt");
        File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
        var md = await Open(c, Fixture("pt01.md", "# A Markdown tab\n\n**bold**\n"));
        var windowId = await WindowIdOf(c, md);
        // The reader's own mode (SourceCodeSetting; SourceCode reads true while a plain text file is shown).
        var source = (await c.Call("test.settings.get", new { windowId, name = "SourceCodeSetting" }))["value"]?.ToString();
        notes.Add("mode setting before: " + (source ?? "(unset)"));
        async Task SetSource(bool on) { await c.Call("test.settings.set", new { windowId, name = "SourceCodeSetting", value = on }); await Task.Delay(1000); }
        try
        {
            await SetSource(false);
            const string state = "JSON.stringify({ codeMirror: !!document.querySelector('.CodeMirror'), muya: !!document.querySelector('#ag-editor-id'), lineNumbers: !!document.querySelector('.CodeMirror-linenumber'), prose: !!document.querySelector('.plain-prose'), highlighted: document.querySelectorAll('.CodeMirror .cm-header, .CodeMirror .cm-strong, .CodeMirror .cm-em, .CodeMirror .cm-variable-2').length })";
            async Task<JObject> State() => JObject.Parse((string)(await c.Call("test.editor.eval", new { windowId, script = state }))["result"]!);
            var txt = await Open(c, path);
            await Task.Delay(1500);
            var visual = await State();
            notes.Add("the .txt, visual mode: " + visual.ToString(Newtonsoft.Json.Formatting.None));
            Check((bool)visual["codeMirror"]! && !(bool)visual["muya"]! && (bool)visual["prose"]! && !(bool)visual["lineNumbers"]!, "visual mode: the .txt laid out as prose, no line numbers, not in the Markdown editor");
            Check((int)visual["highlighted"]! == 0, $"visual mode: no Markdown highlighted ({visual["highlighted"]} marks)");
            Check((string?)(await Get(c, txt))["text"] == text, "its text is the file's, as it is");
            await SetSource(true);
            var code = await State();
            notes.Add("the .txt, source mode: " + code.ToString(Newtonsoft.Json.Formatting.None));
            Check((bool)code["codeMirror"]! && !(bool)code["prose"]! && (bool)code["lineNumbers"]!, "source mode: the .txt as code, with line numbers");
            Check((int)code["highlighted"]! == 0, $"source mode: no Markdown highlighted ({code["highlighted"]} marks)");
            await SetSource(false);
            await TypeIntoWindow(c, windowId, "X");
            string edited = text;
            for (var i = 0; i < 50 && edited == text; i++) { await Task.Delay(200); edited = (string?)(await Get(c, txt))["text"] ?? ""; }
            await c.Call("document.save", new { documentId = txt });
            await Task.Delay(500);
            var saved = File.ReadAllText(path);
            notes.Add("saved: " + saved.Replace("\n", "\\n"));
            Check(saved == text + "X", "the save wrote the key typed and nothing else changed");
            await c.Call("document.focus", new { documentId = md });
            await Task.Delay(1500);
            var markdownTab = await State();
            Check((bool)markdownTab["muya"]! && !(bool)markdownTab["codeMirror"]!, "the Markdown tab is in the visual editor, the reader's mode");
            await c.Call("document.focus", new { documentId = txt });
            await Task.Delay(1500);
            var again = await State();
            Check((bool)again["codeMirror"]! && (bool)again["prose"]! && (int)again["highlighted"]! == 0, "back on the .txt: plain prose again");
            try { await c.Call("document.close", new { documentId = txt }); } catch { }
            // A Markdown block in a plain text file (an md fence, a longer one holding a code block): drawn as Markdown
            // in the prose view, the rest plain.
            var blocks = await Open(c, Fixture("pt01-blocks.txt", "# plain line\n\n````md\n# Block title\n\n**bold**\n\n```js\nconst x = 1\n```\n````\n\nplain **again**\n"));
            await Task.Delay(1500);
            await c.Call("test.editor.eval", new { windowId, script = "(() => { const cm = document.querySelector('.CodeMirror').CodeMirror; cm.setCursor({ line: 0, ch: 0 }); return true })()" });
            await Task.Delay(600);
            var drawn = (string?)(await c.Call("test.editor.eval", new { windowId, script = "JSON.stringify([...document.querySelectorAll('.plain-md-block')].map(b => [b.querySelectorAll('h1').length, b.querySelectorAll('strong').length, b.querySelectorAll('pre').length]))" }))["result"];
            notes.Add("the Markdown block drawn: " + drawn);
            Check(drawn == "[[1,1,1]]", $"the md block drawn as Markdown, its code block inside ({drawn})");
            try { await c.Call("document.close", new { documentId = blocks }); } catch { }
            // Config and data files are plain text too (2026-11-04): a .json and an .ini - its "#" and "*" no Markdown -
            // shown as prose, nothing highlighted; the .ini (UTF-8 with a BOM, CRLF, as Windows writes them) saved with
            // the key typed and every other byte as it was.
            var json = await Open(c, Fixture("pt01.json", "{\n  \"# title\": \"**not bold**\",\n  \"list\": [1, 2]\n}\n"));
            await Task.Delay(1500);
            var jsonState = await State();
            notes.Add("the .json: " + jsonState.ToString(Newtonsoft.Json.Formatting.None));
            Check((bool)jsonState["codeMirror"]! && !(bool)jsonState["muya"]! && (bool)jsonState["prose"]! && (int)jsonState["highlighted"]! == 0, "the .json: plain prose, not in the Markdown editor, nothing highlighted");
            try { await c.Call("document.close", new { documentId = json }); } catch { }
            var iniText = "; settings\r\n[main]\r\n# not a heading\r\nname = *not italic*\r\n\r\nlast = 1";
            var iniPath = Path.Combine(fixtures, "pt01.ini");
            File.WriteAllText(iniPath, iniText, new System.Text.UTF8Encoding(true));
            var iniBefore = File.ReadAllBytes(iniPath);
            var ini = await Open(c, iniPath);
            await Task.Delay(1500);
            var iniState = await State();
            notes.Add("the .ini: " + iniState.ToString(Newtonsoft.Json.Formatting.None));
            Check((bool)iniState["codeMirror"]! && !(bool)iniState["muya"]! && (bool)iniState["prose"]! && (int)iniState["highlighted"]! == 0, "the .ini: plain prose, not in the Markdown editor, nothing highlighted");
            var iniShown = (string?)(await Get(c, ini))["text"] ?? "";
            await TypeIntoWindow(c, windowId, "X");
            for (var i = 0; i < 50 && ((string?)(await Get(c, ini))["text"] ?? "") == iniShown; i++) await Task.Delay(200);
            await c.Call("document.save", new { documentId = ini });
            await Task.Delay(500);
            var iniAfter = File.ReadAllBytes(iniPath);
            notes.Add("the .ini saved: " + BitConverter.ToString(iniAfter.Take(3).ToArray()) + " " + System.Text.Encoding.UTF8.GetString(iniAfter).Replace("\r", "\\r").Replace("\n", "\\n"));
            Check(iniAfter.SequenceEqual(iniBefore.Concat(new[] { (byte)'X' })), "the .ini saved: the key typed, its BOM, CRLF and every other byte as they were");
            try { await c.Call("document.close", new { documentId = ini }); } catch { }
        }
        finally
        {
            try { await SetSource(source == "True" || source == "true"); } catch { }
            try { await c.Call("document.close", new { documentId = md }); } catch { }
        }
    }

    /// <summary>
    /// LR01: a list item dragged by its handle with the mouse, in the app's web view (a reader's request, 2026-11-02):
    /// the last item to the top, then the new last one dragged right under the item above; the file saved has both.
    /// One undo puts the second move back. EditorBench's list-drag-check covers the cases; this is the real pointer.
    /// </summary>
    private static async Task LR01(List<string> notes)
    {
        using var c = await Session("e2e LR01");
        var path = Fixture("lr01.md", "# LR01\n\n- alpha\n- beta\n- gamma\n");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        await WaitUntilDrawn(c, windowId, id, "gamma");
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        // An item's marker (left of its text) and its text's top and bottom, in page coordinates, then on the screen.
        async Task<(int X, int Y, int Top, int Bottom)> Item(string text)
        {
            var r = (await c.Call("test.editor.eval", new { windowId, script = $"(() => {{ const li = [...document.querySelectorAll('#ag-editor-id li')].find(el => {{ const own = [...el.children].find(x => !/^(INPUT|UL|OL)$/.test(x.tagName)); return own && own.textContent.trim() === '{text}' }}); const own = [...li.children].find(x => !/^(INPUT|UL|OL)$/.test(x.tagName)).getBoundingClientRect(); return {{ x: Math.round(own.left - 8), y: Math.round(own.top + own.height / 2), top: Math.round(own.top), bottom: Math.round(own.bottom) }} }})()" }))["result"]!;
            var p = await c.Call("test.editor.screenPoint", new { windowId, x = (int)r["x"]!, y = (int)r["y"]! });
            var top = await c.Call("test.editor.screenPoint", new { windowId, x = (int)r["x"]!, y = (int)r["top"]! });
            var bottom = await c.Call("test.editor.screenPoint", new { windowId, x = (int)r["x"]!, y = (int)r["bottom"]! });
            return ((int)p["x"]!, (int)p["y"]!, (int)top["y"]!, (int)bottom["y"]!);
        }
        async Task<string> Text(string not)
        {
            var text = "";
            for (var i = 0; i < 30; i++) { text = (string?)(await Get(c, id))["text"] ?? ""; if (text != not) break; await Task.Delay(200); }
            return text;
        }
        // The handle an item is dragged by (the marker is none): the pointer moved over the item's text brings it, then
        // where it is on the screen.
        async Task<(int X, int Y)> Handle((int X, int Y, int Top, int Bottom) item)
        {
            await Activate(window);
            int screenW = GetSystemMetrics(0), screenH = GetSystemMetrics(1);
            foreach (var dx in new[] { 60, 50, 40 })
            {
                Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dx = (item.X + dx) * 65535 / (screenW - 1), dy = item.Y * 65535 / (screenH - 1), dwFlags = 0x0001 | 0x8000 } } });
                await Task.Delay(60);
            }
            await Task.Delay(300);
            var h = (await c.Call("test.editor.eval", new { windowId, script = "(() => { const h = document.getElementById('mu-list-drag-handle'); const r = h.getBoundingClientRect(); return getComputedStyle(h).display === 'none' ? null : { x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) } })()" }))["result"];
            if (h == null || h.Type != JTokenType.Object) throw new CaseFailed("no drag handle beside the item under the pointer");
            var p = await c.Call("test.editor.screenPoint", new { windowId, x = (int)h["x"]!, y = (int)h["y"]! });
            return ((int)p["x"]!, (int)p["y"]!);
        }
        try
        {
            var before = (string?)(await Get(c, id))["text"] ?? "";
            var gamma = await Item("gamma");
            var alpha = await Item("alpha");
            var gammaHandle = await Handle(gamma);
            await Drag(window, gammaHandle, (gammaHandle.X, alpha.Top + 2));
            var moved = await Text(before);
            notes.Add("after the first drag: " + moved.Replace("\n", "\\n"));
            Check(moved == "# LR01\n\n- gamma\n- alpha\n- beta\n", "gamma dragged to the top of the list");
            var beta = await Item("beta");
            // Right by a level: under alpha, the item above.
            var scale = GetDpiForWindow(window) / 96.0;
            var betaHandle = await Handle(beta);
            await Drag(window, betaHandle, (betaHandle.X + (int)(40 * scale), beta.Bottom - 2));
            var nested = await Text(moved);
            notes.Add("after the second drag: " + nested.Replace("\n", "\\n"));
            Check(nested == "# LR01\n\n- gamma\n- alpha\n  - beta\n", "beta dragged right goes under alpha");
            await c.Call("document.save", new { documentId = id });
            await Task.Delay(500);
            Check(File.ReadAllText(path).Replace("\r\n", "\n") == nested, "the file saved has both moves");
            await Activate(window);
            await c.Call("test.editor.focus", new { windowId });
            Chord("Ctrl+Z");
            var undone = await Text(nested);
            notes.Add("after one undo: " + undone.Replace("\n", "\\n"));
            Check(undone == moved, "one undo puts the second move back");
        }
        finally
        {
            try { await c.Call("document.save", new { documentId = id }); await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }

    /// <summary>
    /// TR01: closing the tab shown goes back to the tab used before it, as an editor's tabs do (the reader's choice,
    /// 2026-11-02), not to its neighbour in the strip: A, B, C opened, A shown again and closed - C, not B; then C closed
    /// - B.
    /// </summary>
    private static async Task TR01(List<string> notes)
    {
        using var c = await Session("e2e TR01");
        var a = await Open(c, Fixture("tr01-a.md", "# TR01 A\n"));
        var b = await Open(c, Fixture("tr01-b.md", "# TR01 B\n"));
        var cc = await Open(c, Fixture("tr01-c.md", "# TR01 C\n"));
        var windowId = await WindowIdOf(c, a);
        async Task<string> Active() => (string)((JArray)(await c.Call("window.list"))["windows"]!).First(w => (string)w["windowId"]! == windowId)["activeDocumentId"]!;
        async Task<string> WaitActive(Func<string, bool> done)
        {
            var now = await Active();
            for (var i = 0; i < 40 && !done(now); i++) { await Task.Delay(100); now = await Active(); }
            return now;
        }
        string Name(string id) => id == a ? "A" : id == b ? "B" : id == cc ? "C" : id;
        await c.Call("document.focus", new { documentId = a });
        Check(await WaitActive(d => d == a) == a, "A shown again");
        await Task.Delay(500);
        await c.Call("document.close", new { documentId = a });
        var afterA = await WaitActive(d => d != a);
        await Task.Delay(800);
        afterA = await Active();
        notes.Add($"A closed: {Name(afterA)} shown");
        Check(afterA == cc, $"closing A goes back to C, the tab used before it, not B beside it ({Name(afterA)})");
        await c.Call("document.close", new { documentId = cc });
        var afterC = await WaitActive(d => d != cc);
        await Task.Delay(800);
        afterC = await Active();
        notes.Add($"C closed: {Name(afterC)} shown");
        Check(afterC == b, $"then closing C goes back to B ({Name(afterC)})");
        try { await c.Call("document.close", new { documentId = b }); } catch { }
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
