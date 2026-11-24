using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using Newtonsoft.Json.Linq;

/// <summary>
/// OL09 (the user, 2026-11-24): a heading chosen in the outline, then clicked in the document, then Ctrl+A - by a real
/// mouse and keys - and the page stays where the outline put it at every frame (it went a little up on the click, and
/// back to where the caret had been on Ctrl+A: 4509 to 8177 in hp88's log). And a section dropped where a heading in it
/// would go past level 6: refused, and a card at the bottom right names that heading, its level and the one it would be.
/// </summary>
internal static partial class Program
{
    private static async Task OL09(List<string> notes)
    {
        using var c = await Session("e2e OL09");
        string Filler(int n, string tag) => string.Join("\n\n", Enumerable.Range(0, n).Select(i => $"{tag} paragraph {i} with enough words to wrap a little on a narrow page."));
        var original = $"# Doc\n\n{Filler(20, "Top")}\n\n## Checklist\n\n{Filler(60, "Middle")}\n\n## Far below\n\nThe caret is here.\n\n{Filler(20, "End")}\n";
        var id = await Open(c, Fixture("ol09.md", original));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        System.Windows.Rect Row(string heading)
        {
            foreach (AutomationElement item in Deep.All(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem)))
            {
                var text = Deep.First(item, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
                if (text?.Current.Name == heading) return item.Current.BoundingRectangle;
            }
            throw new CaseFailed($"no outline row '{heading}' {Screenshot("OL09-no-row")}");
        }
        // The outline's row chosen as its click does it (TocPage.OnItemInvoked): the jump, then the keyboard to the editor.
        // A real click on the row invoked nothing here (w10 job 728: the row only highlighted, the host logged no jump).
        async Task ClickRow(string heading)
        {
            var slug = (string)(await c.Call("test.editor.eval", new { windowId, script = $"[...document.querySelectorAll('#ag-editor-id h1, #ag-editor-id h2')].find(h => h.textContent.includes({Newtonsoft.Json.JsonConvert.ToString(heading)})).id" }))["result"]!;
            await c.Call("test.outline.jump", new { windowId, slug });
            await c.Call("test.editor.focus", new { windowId });
            await Task.Delay(900);
        }
        async Task<int> ScrollY() => (int)(await c.Call("test.editor.eval", new { windowId, script = "Math.round(window.scrollY)" }))["result"]!;
        async Task Watch() => await c.Call("test.editor.eval", new { windowId, script = "(() => { window.__ys = []; window.__yOn = true; const tick = () => { if (!window.__yOn) return; window.__ys.push(Math.round(window.scrollY)); requestAnimationFrame(tick); }; requestAnimationFrame(tick); return true })()" });
        async Task<int[]> Stop() => ((JArray)(await c.Call("test.editor.eval", new { windowId, script = "(() => { window.__yOn = false; return [...new Set(window.__ys)] })()" }))["result"]!).Select(x => (int)x).ToArray();
        try
        {
            await c.Call("window.setView", new { windowId, mode = "visual", bounds = new { x = 40, y = 40, width = 1300, height = 850 }, sidePane = new { open = true, page = "outline" } });
            await Task.Delay(1200);
            // The caret far below, put there by a click.
            await c.Call("test.editor.eval", new { windowId, script = "(() => { [...document.querySelectorAll('#ag-editor-id .ag-paragraph-content')].find(e => e.textContent === 'The caret is here.').scrollIntoView({ block: 'center' }); return true })()" });
            await Task.Delay(400);
            await ClickEditorText(c, windowId, window, "The caret is here.");
            await Task.Delay(400);
            notes.Add($"the caret far below, the page at {await ScrollY()}");

            // The outline's row, then the heading clicked in the document: the page stays.
            await ClickRow("Checklist");
            var there = await ScrollY();
            var at = (await c.Call("test.editor.eval", new { windowId, script = "(() => { const h = [...document.querySelectorAll('#ag-editor-id h2')].find(h => h.textContent.includes('Checklist')); const r = h.getBoundingClientRect(); return { x: Math.round(r.left + 60), y: Math.round(r.top + r.height / 2) } })()" }))["result"]!;
            var point = await c.Call("test.editor.screenPoint", new { windowId, x = (int)at["x"]!, y = (int)at["y"]! });
            await Watch();
            await Click(window, (int)point["x"]!, (int)point["y"]!);
            await Task.Delay(800);
            var clicked = await Stop();
            notes.Add($"the outline put the page at {there}; the click: {string.Join(",", clicked)}");
            Check(clicked.All(y => Math.Abs(y - there) <= 2), $"the heading clicked after the outline went there: the page stays ({there}: {string.Join(",", clicked)})");

            // The outline again, then Ctrl+A: the page stays.
            // The caret far below again (by a click, the page there), then the outline.
            await c.Call("test.editor.eval", new { windowId, script = "(() => { [...document.querySelectorAll('#ag-editor-id .ag-paragraph-content')].find(e => e.textContent === 'The caret is here.').scrollIntoView({ block: 'center' }); return true })()" });
            await Task.Delay(400);
            await ClickEditorText(c, windowId, window, "The caret is here.");
            await Task.Delay(400);
            await ClickRow("Checklist");
            var again = await ScrollY();
            await Watch();
            Chord("Ctrl+A");
            await Task.Delay(900);
            var all = await Stop();
            notes.Add($"the outline put the page at {again}; Ctrl+A: {string.Join(",", all)}");
            Check(all.All(y => Math.Abs(y - again) <= 2), $"Ctrl+A after the outline went there: the page stays ({again}: {string.Join(",", all)})");
            Chord("Esc");

            // Refused past level 6: the card says which heading and why.
            var deepText = "# Deep\n\n#### A\n\na\n\n###### A deep one\n\nd\n\n#### B\n\nb\n";
            await c.Call("document.replace", new { documentId = id, baseRevision = await Revision(c, id), text = deepText, reveal = "document" });
            await Task.Delay(1500);
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            var from = Row("A");
            var to = Row("B");
            // A press on the outline's empty space first: with the keyboard in the page, the drag's press began nothing
            // (job 731: no "drag begun" in the host's log; a real click on a row in job 728 invoked nothing either).
            await Click(window, (int)(to.Left + 40), (int)(to.Bottom + 90));
            await Task.Delay(400);
            await Drag(window, ((int)(from.Left + 40), (int)(from.Top + from.Height / 2)), ((int)(to.Left + 40), (int)(to.Top + to.Height * 0.5)), steps: 20);
            AutomationElement? said = null;
            await Eventually(async () => { await Task.CompletedTask; return (said = FindIn(window, "OutlineMoveNoticeText")) != null; }, 4000, 200);
            notes.Add("refused, the card: " + said?.Current.Name + " " + Screenshot("OL09-refused"));
            Check(said != null && said.Current.Name.Contains("A deep one") && said.Current.Name.Contains("6") && said.Current.Name.Contains("7"), "refused past level 6: the card names the heading, its level and the one it would be: " + said?.Current.Name);
            Check((string)(await Get(c, id))["text"]! == deepText, "and the document is as it was");
        }
        finally
        {
            try { Chord("Esc"); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
