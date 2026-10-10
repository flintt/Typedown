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
        // The caret into the plain paragraph.
        await c.Call("test.editor.eval", new { windowId, script = "(() => { const p = [...document.querySelectorAll('#ag-editor-id p')].find(x => x.textContent.includes('plain')); const r = document.createRange(); r.setStart(p.firstChild || p, 3); r.collapse(true); const s = getSelection(); s.removeAllRanges(); s.addRange(r); p.dispatchEvent(new Event('click', { bubbles: true })); return true })()" });
        await Task.Delay(800);
        var tableBefore = await MenuToggle(window, "TableItem");
        notes.Add($"Table before: {tableBefore}");
        Check(tableBefore == ToggleState.Off, "Table is not checked with the caret in a plain paragraph");

        await InvokeMenuBarItem(window, "TableItem");
        await Task.Delay(1200);
        Send(Key(0x1B, false), Key(0x1B, true)); // Esc: the insert-table dialog cancelled
        await Task.Delay(1000);
        var tableAfter = await MenuToggle(window, "TableItem");
        notes.Add($"Table after its dialog was cancelled: {tableAfter}");
        Check(tableAfter == ToggleState.Off, "Table is still not checked after its dialog was cancelled");

        await InvokeMenuBarItem(window, "QuoteItem");
        await Task.Delay(1200);
        var quote = await MenuToggle(window, "QuoteItem");
        notes.Add($"Quote after it was chosen: {quote}");
        Check(quote == ToggleState.On, "Quote is checked once the paragraph is a quote");
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
