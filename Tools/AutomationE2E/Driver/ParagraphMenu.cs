using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Automation;

/// <summary>
/// PM01: the Paragraph menu's checks show where the caret is, not what was last clicked. Table clicked and its dialog
/// cancelled left Table checked, with no table anywhere; Quote, which does turn the paragraph into a quote, is checked
/// after it.
/// </summary>
internal static partial class Program
{
    private static async Task PM01(List<string> notes)
    {
        using var c = await Session("e2e PM01");
        var id = await Open(c, Fixture("pm01.md", "# PM01\n\nA plain paragraph.\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        await Activate(window);
        await Task.Delay(1000);
        // The caret into the plain paragraph, with a click as a person puts it there: the menu's commands act on the
        // editor only while it has the focus.
        await ClickEditorText(c, windowId, window, "plain");
        await Task.Delay(800);
        var tableBefore = await MenuToggle(window, "TableItem");
        notes.Add($"Table before: {tableBefore}");
        Check(tableBefore == ToggleState.Off, "Table is not checked with the caret in a plain paragraph");

        // Reading the check opened and closed the menus, which took the focus from the editor: back into it first.
        await ClickEditorText(c, windowId, window, "plain");
        await Task.Delay(500);
        await InvokeMenuBarItem(window, "TableItem", escapeAfter: false);
        var opened = false;
        for (var i = 0; i < 20 && !opened; i++) { await Task.Delay(150); opened = (bool)(await c.Call("test.dialog.colours", new { windowId }))["open"]!; }
        Check(opened, "Table opened the insert-table dialog");
        Send(Key(0x1B, false), Key(0x1B, true)); // Esc: the insert-table dialog cancelled
        await Task.Delay(1000);
        var tableAfter = await MenuToggle(window, "TableItem");
        notes.Add($"Table after its dialog was cancelled: {tableAfter}");
        Check(tableAfter == ToggleState.Off, "Table is still not checked after its dialog was cancelled");

        await ClickEditorText(c, windowId, window, "plain");
        await Task.Delay(500);
        await InvokeMenuBarItem(window, "QuoteItem", escapeAfter: false);
        await Task.Delay(1200);
        var text = (string?)(await Get(c, id))["text"] ?? "";
        notes.Add($"text after Quote: {text.Replace("\n", "\\n")}");
        Check(text.Contains("> A plain paragraph"), "Quote turned the paragraph into a quote");
        await ClickEditorText(c, windowId, window, "plain");
        await Task.Delay(500);
        var quote = await MenuToggle(window, "QuoteItem");
        notes.Add($"Quote after it was chosen: {quote}");
        Check(quote == ToggleState.On, "Quote is checked once the paragraph is a quote");
    }

    // A click on the middle of the editor's paragraph holding the text, as a person clicks it.
    private static async Task ClickEditorText(Client c, string windowId, IntPtr window, string text)
    {
        await Activate(window);
        var at = (await c.Call("test.editor.eval", new { windowId, script = "(() => { const p = [...document.querySelectorAll('#ag-editor-id p, #ag-editor-id h1')].find(x => x.textContent.includes(" + Newtonsoft.Json.JsonConvert.ToString(text) + ")); if (!p) return null; const r = p.getBoundingClientRect(); return { x: Math.round(r.left + Math.min(40, r.width / 2)), y: Math.round(r.top + r.height / 2) } })()" }))["result"];
        if (at == null || at.Type != Newtonsoft.Json.Linq.JTokenType.Object) throw new CaseFailed($"no paragraph with '{text}' on the page");
        var screen = await c.Call("test.editor.screenPoint", new { windowId, x = (int)at["x"]!, y = (int)at["y"]! });
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        SetCursorPos((int)screen["x"]!, (int)screen["y"]!);
        await Task.Delay(150);
        Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = 0x0002 } } });
        await Task.Delay(60);
        Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = 0x0004 } } });
    }

    // A menu bar item's check, read with its menu open (each menu opened until the one holding it), then closed.
    private static async Task<ToggleState?> MenuToggle(IntPtr window, string automationId)
    {
        await Activate(window);
        var root = AutomationElement.FromHandle(window);
        var bar = Deep.First(root, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuBar),
            new PropertyCondition(AutomationElement.FrameworkIdProperty, "XAML"))) ?? throw new CaseFailed("no menu bar in the window");
        foreach (AutomationElement top in bar.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem)))
        {
            if (!top.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expand)) continue;
            ((ExpandCollapsePattern)expand).Expand();
            await Task.Delay(400);
            var item = Deep.First(root, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
            ToggleState? state = null;
            if (item != null && item.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle)) state = ((TogglePattern)toggle).Current.ToggleState;
            try { ((ExpandCollapsePattern)expand).Collapse(); } catch (Exception) { }
            await Task.Delay(250);
            if (item != null) return state;
        }
        throw new CaseFailed($"no menu item {automationId}");
    }
}
