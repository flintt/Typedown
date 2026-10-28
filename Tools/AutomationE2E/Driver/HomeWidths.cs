using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;

/// <summary>
/// HP06: the home page's recent files show their time whole at every window width. A window with a file of a long name
/// just closed (the home page shows, the file first in Recent files) is made 480 to 1400 pixels wide, 40 at a time; at
/// each width the first entry's time (its last text, at the right) must lie inside the list, not cut off at its right edge
/// - at some widths it was out of sight.
/// </summary>
internal static partial class Program
{
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowPos")] private static extern bool HwSetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    private static async Task HP06(List<string> notes)
    {
        using var c = await Session("e2e HP06");
        var windowId = (string)(await c.Call("test.window.open"))["windowId"]!;
        var window = await WindowOf(windowId, c);
        try
        {
            await Task.Delay(3000);
            var name = "hp06 a rather long file name for the recent list on the home page.md";
            await c.Call("document.open", new { path = Fixture(name, "# HP06\n"), windowId, reveal = "document" });
            await Task.Delay(1500);
            await Activate(window);
            Chord("Ctrl+W");
            Check(await HomeShown(c, windowId, true, 6000), "closing the file shows the home page");
            var cut = new List<int>();
            for (var width = 480; width <= 1400; width += 40)
            {
                HwSetWindowPos(window, IntPtr.Zero, 0, 0, width, 760, 0x0002 | 0x0004); // no move, no z-order
                await Task.Delay(500);
                var list = Deep.First(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.AutomationIdProperty, "HomeRecentFiles"));
                if (list == null) { notes.Add($"{width}: no recent list"); cut.Add(width); continue; }
                var item = Deep.All(list, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)).Cast<AutomationElement>().FirstOrDefault();
                if (item == null) { notes.Add($"{width}: no entry in the recent list"); cut.Add(width); continue; }
                var texts = Deep.All(item, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)).Cast<AutomationElement>()
                    .Where(t => !t.Current.BoundingRectangle.IsEmpty).OrderBy(t => t.Current.BoundingRectangle.Left).ToList();
                var time = texts.LastOrDefault();
                var listRect = list.Current.BoundingRectangle;
                var timeRect = time?.Current.BoundingRectangle ?? System.Windows.Rect.Empty;
                var whole = time != null && timeRect.Width > 0 && timeRect.Right <= listRect.Right + 0.5 && timeRect.Left >= listRect.Left;
                notes.Add($"{width}: list {listRect.Left:0}-{listRect.Right:0}, time \"{time?.Current.Name}\" {timeRect.Left:0}-{timeRect.Right:0}{(whole ? "" : " CUT")}");
                if (!whole) cut.Add(width);
            }
            Check(cut.Count == 0, "the first recent file's time is whole at every width" + (cut.Count > 0 ? $" (cut at {string.Join(", ", cut)})" : ""));
        }
        finally
        {
            // Ctrl+W on the home page closes the window (HP03).
            if (IsWindow(window)) { await Activate(window); Chord("Ctrl+W"); await Task.Delay(1000); }
        }
    }
}
