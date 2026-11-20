using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// HD01 (the user's 18, 2026-11-20): View > Hide Done Items, turned on in the real menu: in reading mode the items struck
/// through whole and the ticked tasks are folded, each run one line that a real click opens; an ordered list keeps its
/// numbers; a paragraph with a few words struck stays; the text is never changed. Off again, nothing is hidden.
/// </summary>
internal static partial class Program
{
    private static async Task HD01(List<string> notes)
    {
        const string original = "# HD01\n\n1. one\n2. ~~two~~\n3. ~~three~~\n4. four\n\n- [x] done task\n- [ ] open task\n\nSome ~~partly~~ struck.\n";
        using var c = await Session("e2e HD01");
        var id = await Open(c, Fixture("hd01.md", original));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        // Each block by its own text: its height and the line standing for its run, as the page draws them.
        async Task<JObject> Shown()
        {
            var r = (string?)(await c.Call("test.editor.eval", new { windowId, script = @"JSON.stringify((() => {
                const own = el => { const c = el.cloneNode(true); c.querySelectorAll('ul, ol, .ag-hide').forEach(x => x.remove()); return [...c.querySelectorAll('.ag-paragraph-content')].map(p => p.textContent).join('').trim(); };
                const out = {};
                for (const el of document.querySelectorAll('#ag-editor-id li, #ag-editor-id p:not(li p)'))
                    out[own(el)] = { height: Math.round(el.getBoundingClientRect().height), run: el.dataset.hiddenRun || null };
                return out; })())" }))["result"];
            return JObject.Parse(r ?? "{}");
        }
        static int Height(JObject s, string key) => (int?)s[key]?["height"] ?? -1;
        static string? Run(JObject s, string key) => (string?)s[key]?["run"];
        try
        {
            await c.Call("window.setView", new { windowId, mode = "visual", bounds = new { x = 40, y = 40, width = 1300, height = 850 } });
            await Task.Delay(800);
            Check(await c.Call("test.settings.get", new { windowId, name = "HideDone" }) is { } off && (bool?)off["value"] == false, "the switch starts off");

            // View > Hide Done Items, by the real menu; then reading mode.
            await InvokeMenuBarItem(window, "HideDoneItem");
            await Task.Delay(400);
            Check((bool?)(await c.Call("test.settings.get", new { windowId, name = "HideDone" }))["value"] == true, "View > Hide Done Items turns the switch on");
            await c.Call("test.settings.set", new { windowId, name = "ReadOnly", value = true });
            JObject s = new();
            await Eventually(async () => Run(s = await Shown(), "two") != null, 5000, 200);
            notes.Add("reading mode: " + s.ToString(Newtonsoft.Json.Formatting.None));
            Check(Run(s, "two") is { } line && line.Contains('2') && Height(s, "three") == 0, "two struck items in a row: one line for both (\"" + Run(s, "two") + "\"), the second of no height");
            Check(Height(s, "one") > 0 && Height(s, "four") > 0, "the items around stay");
            Check(Run(s, "done task") != null && Height(s, "open task") > 0, "a ticked task folded, an open one shown");
            Check(Height(s, "Some partly struck.") > 0 && Run(s, "Some partly struck.") == null, "a paragraph with a few words struck stays");
            notes.Add("shot, reading mode folded: " + Screenshot("HD01-1-reading-folded"));

            // A real click on the line opens its run.
            // The line's box, and what the page has at the point clicked (run 687: the click came to the editor itself).
            var at = (await c.Call("test.editor.eval", new { windowId, script = "(() => { const li = [...document.querySelectorAll('#ag-editor-id li')].find(l => l.dataset.hiddenRun && l.textContent.includes('two')); const r = li.getBoundingClientRect(); const x = Math.round(r.left + 20), y = Math.round(r.top + r.height / 2); const e = document.elementFromPoint(x, y); return { x, y, box: [Math.round(r.left), Math.round(r.top), Math.round(r.width), Math.round(r.height)], there: e ? e.tagName + '#' + e.id + '.' + String(e.className).slice(0, 30) : null } })()" }))["result"]!;
            notes.Add("the line: " + at.ToString(Newtonsoft.Json.Formatting.None));
            var screen = await c.Call("test.editor.screenPoint", new { windowId, x = (int)at["x"]!, y = (int)at["y"]! });
            // Click takes the point on the screen as it is: the window's place taken off it, the click landed 40 px up and
            // left of the line (runs 686-691, the page got it at 102,92 for 142,132).
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            // What the page gets of the click (run 686: a real click there did not open the run).
            await c.Call("test.editor.eval", new { windowId, script = "(() => { window.__hd01 = []; for (const t of ['pointerdown', 'mousedown', 'click']) document.addEventListener(t, e => window.__hd01.push(t + ' ' + e.target.tagName + '#' + (e.target.id || '') + '.' + String(e.target.className || '').slice(0, 40) + ' at ' + e.clientX + ',' + e.clientY), true); return true })()" });
            await Click(window, (int)screen["x"]!, (int)screen["y"]!);
            await Eventually(async () => Height(s = await Shown(), "three") > 0, 4000, 200);
            notes.Add($"clicked at page {at.ToString(Newtonsoft.Json.Formatting.None)}, screen {screen.ToString(Newtonsoft.Json.Formatting.None)}: the page got {(string?)(await c.Call("test.editor.eval", new { windowId, script = "JSON.stringify(window.__hd01 || [])" }))["result"]}");
            notes.Add("shot, after the click (opened, with its line to fold it again): " + Screenshot("HD01-2-opened"));
            Check(Height(s, "two") > 0 && Height(s, "three") > 0, "clicked, the run opens");
            Check(Run(s, "done task") != null, "the other runs stay folded");

            // The text never changed. The visual editor folds too (the caret elsewhere); off again, nothing hidden.
            Check((string)(await Get(c, id))["text"]! == original, "the text is unchanged");
            await c.Call("test.settings.set", new { windowId, name = "ReadOnly", value = false });
            await Task.Delay(800);
            notes.Add("shot, the visual editor folded: " + Screenshot("HD01-3-visual-folded"));
            await InvokeMenuBarItem(window, "HideDoneItem");
            await c.Call("test.settings.set", new { windowId, name = "ReadOnly", value = true });
            await Task.Delay(800);
            s = await Shown();
            Check(Run(s, "done task") == null && Height(s, "done task") > 0, "the switch off: nothing hidden in reading mode");
        }
        finally
        {
            try { await c.Call("test.settings.set", new { windowId, name = "HideDone", value = false }); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "ReadOnly", value = false }); } catch { }
            try { await c.Call("document.close", new { documentId = id, discard = true }); } catch { }
        }
    }
}
