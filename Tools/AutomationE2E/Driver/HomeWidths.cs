using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;

/// <summary>
/// HP06: the home page's recent files show their time whole at every window width. A window with a file of a long name
/// just closed (the home page shows, the file first in Recent files) is made 480 to 1400 pixels wide, 20 at a time; at
/// each width the first entry's time (its last text, at the right) must show as wide as at the widest and inside the list -
/// at some widths it was cut off at the list's right edge.
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
            // Each width's time: where it is, and how wide it shows. All the same text, so one shown narrower than the widest
            // was cut (at 880 pixels on w10 it showed 32 of its 43 pixels, against the list's right edge, and passed a
            // check that only asked whether it ended inside the list).
            var seen = new List<(int Width, System.Windows.Rect List, System.Windows.Rect Time, string Text)>();
            var cut = new List<int>();
            for (var width = 480; width <= 1400; width += 20)
            {
                HwSetWindowPos(window, IntPtr.Zero, 0, 0, width, 760, 0x0002 | 0x0004); // no move, no z-order
                await Task.Delay(400);
                var list = Deep.First(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.AutomationIdProperty, "HomeRecentFiles"));
                var item = list == null ? null : Deep.All(list, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)).Cast<AutomationElement>().FirstOrDefault();
                var time = item == null ? null : Deep.All(item, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)).Cast<AutomationElement>()
                    .Where(t => !t.Current.BoundingRectangle.IsEmpty).OrderBy(t => t.Current.BoundingRectangle.Left).LastOrDefault();
                if (time == null) { notes.Add($"{width}: no recent entry or no time"); cut.Add(width); continue; }
                seen.Add((width, list!.Current.BoundingRectangle, time.Current.BoundingRectangle, time.Current.Name));
                // Where the window is, and the first cut one's picture: what takes the page's left there.
                GetWindowRect(window, out var w);
                var listNow = list.Current.BoundingRectangle;
                if (cut.Count == 0 && time.Current.BoundingRectangle.Right > listNow.Right - 1)
                    notes.Add($"{width}: window {w.Left}-{w.Right}, screenshot {Screenshot("hp06-" + width)}");
                if (width % 100 == 0 || width is 840 or 860) notes.Add($"{width}: window {w.Left}-{w.Right}");
            }
            var full = seen.Count > 0 ? seen.Max(x => x.Time.Width) : 0;
            foreach (var (width, listRect, timeRect, text) in seen)
            {
                var whole = timeRect.Width >= full - 1 && timeRect.Right <= listRect.Right - 1 && timeRect.Left >= listRect.Left;
                notes.Add($"{width}: list {listRect.Left:0}-{listRect.Right:0}, time \"{text}\" {timeRect.Left:0}-{timeRect.Right:0} ({timeRect.Width:0} of {full:0}){(whole ? "" : " CUT")}");
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
