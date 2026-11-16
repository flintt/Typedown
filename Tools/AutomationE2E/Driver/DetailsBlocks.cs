using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// DB01: a &lt;details&gt; block (Settings &gt; Editor &gt; Foldable &lt;details&gt; blocks, on; Typedown issue #1) in the
/// window: the issue's example one block, closed, its summary shown; opened by a real click on its toggle and closed by
/// one on its summary, the document not made unsaved by either; saved, the file as it was; the setting on the settings
/// page.
/// </summary>
internal static partial class Program
{
    private const string DetailsIssue = "## 参考点1\n\n<details>\n<summary>example111</summary>\n\n- abc\n\n> 123\n\n</details>\n\n## 参考点2\n";

    private static async Task DB01(List<string> notes)
    {
        var path = Fixture("db01.md", DetailsIssue);
        using var c = await Session("e2e DB01");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        // The block as the page shows it: open or not, its summary, how many of its blocks are shown.
        async Task<(bool Found, bool Open, string Summary, int Shown)> Block()
        {
            var r = (await c.Call("test.editor.eval", new { windowId, script = "(() => { const f = document.querySelector('#ag-editor-id figure[data-role=\"DETAILS\"]'); if (!f) return null; return { open: f.classList.contains('ag-details-open'), summary: f.querySelector(':scope > .ag-details-summary').textContent, shown: [...f.children].filter(x => !x.matches('.ag-details-toggle, .ag-details-summary, .ag-front-icon') && getComputedStyle(x).display !== 'none').length } })()" }))["result"];
            return r == null || r.Type != Newtonsoft.Json.Linq.JTokenType.Object ? (false, false, "", 0) : (true, (bool)r["open"]!, (string?)r["summary"] ?? "", (int)r["shown"]!);
        }
        // A real click on a part of the block, found on the page.
        async Task ClickPart(string selector)
        {
            var at = (await c.Call("test.editor.eval", new { windowId, script = "(() => { const e = document.querySelector('#ag-editor-id figure[data-role=\"DETAILS\"] > " + selector + "'); if (!e) return null; const r = e.getBoundingClientRect(); return { x: Math.round(r.left + Math.min(10, r.width / 2)), y: Math.round(r.top + r.height / 2) } })()" }))["result"];
            if (at == null || at.Type != Newtonsoft.Json.Linq.JTokenType.Object) throw new CaseFailed($"no {selector} on the page");
            var screen = await c.Call("test.editor.screenPoint", new { windowId, x = (int)at["x"]!, y = (int)at["y"]! });
            await Activate(window);
            await Click(window, (int)screen["x"]!, (int)screen["y"]!);
            await Task.Delay(500);
        }
        try
        {
            await c.Call("document.focus", new { documentId = id });
            Check(await Eventually(async () => (await Block()).Found, 8000), "the issue's example is one block on the page");
            var shown = await Block();
            notes.Add($"shown: open {shown.Open}, summary '{shown.Summary}', blocks shown {shown.Shown}");
            Check(!shown.Open && shown.Summary == "example111" && shown.Shown == 0, "closed, its summary shown, nothing in it shown");

            await ClickPart(".ag-details-toggle");
            var opened = await Block();
            notes.Add($"after the toggle: open {opened.Open}, blocks shown {opened.Shown}");
            Check(opened.Open && opened.Shown == 2, "a click on its toggle opens it: the list and the quotation shown");
            await ClickPart(".ag-details-summary");
            var closed = await Block();
            Check(!closed.Open && closed.Shown == 0, "a click on its summary closes it");
            var doc = await Get(c, id);
            Check((bool)doc["saved"]! && Lines((string?)doc["text"]) == DetailsIssue, "opening and closing it leaves the document as it was, saved");

            await c.Call("document.save", new { documentId = id });
            Check(Disk(path) == DetailsIssue, "saved, the file is as it was");

            await c.Call("test.window.navigate", new { windowId, route = "Settings/Editor" });
            var toggle = await WaitFor(window, "DetailsBlocksToggle");
            var on = toggle.TryGetCurrentPattern(System.Windows.Automation.TogglePattern.Pattern, out var p) && ((System.Windows.Automation.TogglePattern)p).Current.ToggleState == System.Windows.Automation.ToggleState.On;
            Check(on, "Settings > Editor has the setting, on");
        }
        finally
        {
            try { await c.Call("test.window.navigate", new { windowId, route = "Main" }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }

    /// <summary>
    /// DB02: a &lt;details&gt; block made and edited as a person does (detailsCtrl): Paragraph &gt; Details block wraps the
    /// paragraph the caret is in, the caret in its empty summary, and the menu item shows it checked; the summary typed,
    /// Enter goes into the block; a real double click on the summary edits it; saved, the file has the block as typed.
    /// </summary>
    private static async Task DB02(List<string> notes)
    {
        var path = Fixture("db02.md", "Hello\n");
        using var c = await Session("e2e DB02");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        async Task<string> Text() => Lines((string?)(await Get(c, id))["text"]);
        // Paragraph > (an item), clicked: each top menu opened until the item is there.
        async Task MenuClick(string automationId)
        {
            await Activate(window);
            var root = System.Windows.Automation.AutomationElement.FromHandle(window);
            var bar = Deep.First(root, new System.Windows.Automation.AndCondition(
                new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.MenuBar),
                new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.FrameworkIdProperty, "XAML"))) ?? throw new CaseFailed("no menu bar in the window");
            foreach (System.Windows.Automation.AutomationElement top in bar.FindAll(System.Windows.Automation.TreeScope.Children, new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.MenuItem)))
            {
                if (!top.TryGetCurrentPattern(System.Windows.Automation.ExpandCollapsePattern.Pattern, out var expand)) continue;
                ((System.Windows.Automation.ExpandCollapsePattern)expand).Expand();
                await Task.Delay(400);
                var item = Deep.First(root, new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.AutomationIdProperty, automationId));
                if (item != null) { await ClickInPopup(window, item); await Task.Delay(600); return; }
                try { ((System.Windows.Automation.ExpandCollapsePattern)expand).Collapse(); } catch (Exception) { }
                await Task.Delay(250);
            }
            throw new CaseFailed($"no menu item {automationId}");
        }
        try
        {
            await c.Call("document.focus", new { documentId = id });
            await ClickEditorText(c, windowId, window, "Hello");
            await MenuClick("DetailsBlockItem");
            Check(await Eventually(async () => (await Text()) == "<details>\n<summary></summary>\n\nHello\n\n</details>\n", 6000), "Paragraph > Details block wraps the paragraph");
            TypeText("Title");
            Check(await Eventually(async () => (await Text()).Contains("<summary>Title</summary>"), 4000), "the caret in its empty summary: the summary typed");
            var checkedState = await MenuToggleEventually(window, "DetailsBlockItem", System.Windows.Automation.ToggleState.On);
            Check(checkedState == System.Windows.Automation.ToggleState.On, "in the block the menu item is checked");

            await c.Call("test.editor.focus", new { windowId });
            Chord("Enter");
            await Task.Delay(400);
            TypeText("Z");
            Check(await Eventually(async () => (await Text()).Contains("\nZHello\n"), 4000), "Enter in the summary: the caret at the start of what is in the block");
            Check(!(await Text()).Contains("Title\n</summary>"), "no line break in the summary");

            // A real double click on the summary edits it.
            var at = (await c.Call("test.editor.eval", new { windowId, script = "(() => { const r = document.querySelector('#ag-editor-id figure[data-role=\"DETAILS\"] > .ag-details-summary').getBoundingClientRect(); return { x: Math.round(r.left + 12), y: Math.round(r.top + r.height / 2) } })()" }))["result"]!;
            var screen = await c.Call("test.editor.screenPoint", new { windowId, x = (int)at["x"]!, y = (int)at["y"]! });
            await Activate(window);
            await Click(window, (int)screen["x"]!, (int)screen["y"]!, clicks: 2);
            await Task.Delay(500);
            Chord("End");
            TypeText("!");
            Check(await Eventually(async () => (await Text()).Contains("<summary>Title!</summary>"), 4000), "a double click on the summary edits it");
            var open = (bool?)(await c.Call("test.editor.eval", new { windowId, script = "document.querySelector('#ag-editor-id figure[data-role=\"DETAILS\"]').classList.contains('ag-details-open')" }))["result"];
            Check(open == true, "and leaves it open");

            await c.Call("document.save", new { documentId = id });
            var disk = Lines(Disk(path));
            notes.Add("saved: " + disk.Replace("\n", "\\n"));
            Check(disk == "<details>\n<summary>Title!</summary>\n\nZHello\n\n</details>\n", "saved, the file has the block as typed");
        }
        finally
        {
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
