using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;

/// <summary>
/// TW01: the mouse wheel over the tab strip, with General > "Mouse wheel goes round the tabs" off and on. Off, a notch
/// down on the last tab leaves it shown and a notch up shows the one before; on, a notch down on the last tab shows the
/// first. The tabs are read from the strip in their order; other cases may have left theirs open, which only adds tabs.
/// </summary>
internal static partial class Program
{
    private static async Task TW01(List<string> notes)
    {
        using var c = await Session("e2e TW01");
        var a = await Open(c, Fixture("tw01-a.md", "# TW01 a\n"));
        var b = await Open(c, Fixture("tw01-b.md", "# TW01 b\n"));
        var windowId = await WindowIdOf(c, b);
        var window = await WindowOf(windowId, c);
        var wrapsBefore = (bool?)(await c.Call("test.settings.get", new { windowId, name = "TabWheelWraps" }))["value"];
        List<AutomationElement> Tabs() => Deep.All(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem)).Cast<AutomationElement>().ToList();
        int Selected(List<AutomationElement> tabs) => tabs.FindIndex(t => t.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var p) && ((SelectionItemPattern)p).Current.IsSelected);
        async Task<int> SelectedAfterWheel(int delta)
        {
            var tabs = Tabs();
            var r = tabs[Selected(tabs)].Current.BoundingRectangle;
            Wheel((int)(r.Left + r.Width / 2), (int)(r.Top + r.Height / 2), delta);
            await Task.Delay(800);
            return Selected(Tabs());
        }
        async Task ShowLast()
        {
            var tabs = Tabs();
            ((SelectionItemPattern)tabs[^1].GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            Check(await Eventually(async () => { await Task.Yield(); return Selected(Tabs()) == Tabs().Count - 1; }, 3000), "the last tab is shown");
        }
        try
        {
            await Activate(window);
            var count = Tabs().Count;
            notes.Add($"{count} tabs in the strip");
            Check(count >= 2, "two tabs or more in the strip");

            await c.Call("test.settings.set", new { windowId, name = "TabWheelWraps", value = false });
            await ShowLast();
            var down = await SelectedAfterWheel(-120);
            notes.Add($"off, a notch down on the last tab: tab {down + 1} of {count}");
            Check(down == count - 1, "off: a notch down on the last tab leaves it shown");
            var up = await SelectedAfterWheel(120);
            notes.Add($"off, a notch up: tab {up + 1} of {count}");
            Check(up == count - 2, "off: a notch up shows the tab before");

            await c.Call("test.settings.set", new { windowId, name = "TabWheelWraps", value = true });
            await ShowLast();
            var round = await SelectedAfterWheel(-120);
            notes.Add($"on, a notch down on the last tab: tab {round + 1} of {count}");
            Check(round == 0, "on: a notch down on the last tab shows the first");
        }
        finally
        {
            if (wrapsBefore != null) await c.Call("test.settings.set", new { windowId, name = "TabWheelWraps", value = wrapsBefore });
            foreach (var id in new[] { a, b }) try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
