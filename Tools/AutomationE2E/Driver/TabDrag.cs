using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;

/// <summary>
/// TD01: a tab dragged to another place in the strip looks as the other tabs do once it is let go (the user's list,
/// 2026-11-11, 7: it kept the look it had while dragged). Three documents, the last shown; the first dragged past the
/// last with a real mouse, and the mouse moved away: the tab moved and the other tab not shown have the same background
/// and text colours, as they had before. Screenshots while dragged and after (tabdrag-*.png).
/// </summary>
internal static partial class Program
{
    private static async Task TD01(List<string> notes)
    {
        var folder = FixtureFolder("td01");
        var paths = Enumerable.Range(1, 3).Select(i => Path.Combine(folder, $"td01-{i}.md")).ToList();
        for (var i = 0; i < paths.Count; i++) File.WriteAllText(paths[i], $"# td01 document {i + 1}\n");
        using var c = await Session("e2e TD01");
        var ids = new List<string>();
        foreach (var path in paths) ids.Add(await Open(c, path));
        var windowId = await WindowIdOf(c, ids[0]);
        var window = await WindowOf(windowId, c);
        await c.Call("test.settings.set", new { windowId, name = "AlwaysShowTabBar", value = true });
        List<AutomationElement> Tabs() => Deep.All(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem)).Cast<AutomationElement>()
            .Where(t => t.Current.Name.Contains("td01-")).ToList();
        AutomationElement TabOf(string name) => Tabs().First(t => t.Current.Name.Contains(name));
        // A tab's look: the colour most of its left part has (its background) and the darkest or lightest one away from
        // it (its text), read off the screen.
        (Color back, Color text) Look(AutomationElement tab)
        {
            var r = tab.Current.BoundingRectangle;
            var area = new Rectangle((int)r.Left + 4, (int)r.Top + 4, Math.Max(8, (int)(r.Width * 0.6)), Math.Max(4, (int)r.Height - 8));
            using var bitmap = new Bitmap(area.Width, area.Height);
            using (var g = Graphics.FromImage(bitmap)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
            var counts = new Dictionary<int, int>();
            for (var x = 0; x < area.Width; x += 2)
                for (var y = 0; y < area.Height; y += 2)
                {
                    var p = bitmap.GetPixel(x, y).ToArgb();
                    counts[p] = counts.GetValueOrDefault(p) + 1;
                }
            var back = Color.FromArgb(counts.OrderByDescending(kv => kv.Value).First().Key);
            int Distance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
            var text = counts.Keys.Select(Color.FromArgb).OrderByDescending(col => Distance(col, back)).First();
            return (back, text);
        }
        string Show((Color back, Color text) l) => $"back #{l.back.R:X2}{l.back.G:X2}{l.back.B:X2} text #{l.text.R:X2}{l.text.G:X2}{l.text.B:X2}";
        bool Near(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) <= 24;
        try
        {
            await Eventually(() => Task.FromResult(Tabs().Count == 3), 8000);
            var order = Tabs().Select(t => t.Current.Name).ToList();
            notes.Add("tabs: " + string.Join(" | ", order));
            Check(order.Count == 3, "three documents in three tabs");
            var names = paths.Select(p => Path.GetFileNameWithoutExtension(p)!).ToList();
            var first = names.First(n => order[0].Contains(n));
            var middle = names.First(n => order[1].Contains(n));
            var last = names.First(n => order[2].Contains(n));
            // The last shown, the mouse away from the strip.
            await Click(window, TabOf(last));
            var editor = CenterOf(AutomationElement.FromHandle(window));
            await Click(window, editor.Item1, editor.Item2 + 120);
            await Task.Delay(600);
            var before = (moved: Look(TabOf(first)), other: Look(TabOf(middle)));
            notes.Add($"before: {first} {Show(before.moved)}; {middle} {Show(before.other)}");

            // Dragged past the last, with a picture while it is held.
            var from = CenterOf(TabOf(first));
            var lastRect = TabOf(last).Current.BoundingRectangle;
            var to = ((int)(lastRect.Right - 6), from.Item2);
            await Drag(window, from, to, steps: 20);
            await Task.Delay(400);
            notes.Add("released: " + Screenshot("tabdrag-released"));
            // The mouse moved away, as a reader does after a drag.
            SetCursorPos(editor.Item1, editor.Item2 + 160);
            await Task.Delay(800);
            notes.Add("after: " + Screenshot("tabdrag-after"));
            var after = Tabs().Select(t => t.Current.Name).ToList();
            notes.Add("tabs after: " + string.Join(" | ", after));
            Check(after.Count == 3 && after[2].Contains(first), $"the first tab is now the last ({string.Join(" | ", after)})");
            var now = (moved: Look(TabOf(first)), other: Look(TabOf(middle)));
            notes.Add($"after: {first} {Show(now.moved)}; {middle} {Show(now.other)}");
            Check(Near(now.moved.back, now.other.back) && Near(now.moved.text, now.other.text),
                $"the tab moved looks as the other tab not shown does ({Show(now.moved)} against {Show(now.other)})");
            Check(Near(now.moved.back, before.moved.back) && Near(now.moved.text, before.moved.text),
                $"and as it did before the drag ({Show(now.moved)} against {Show(before.moved)})");

            // The tab shown dragged to the start: still drawn as the tab shown, and the others as they were.
            var shownBefore = Look(TabOf(last));
            var otherBefore = Look(TabOf(middle));
            notes.Add($"shown tab before: {Show(shownBefore)}; other {Show(otherBefore)}");
            var shownFrom = CenterOf(TabOf(last));
            var startRect = Tabs().First().Current.BoundingRectangle;
            await Drag(window, shownFrom, ((int)(startRect.Left + 6), shownFrom.Item2), steps: 20);
            await Task.Delay(400);
            SetCursorPos(editor.Item1, editor.Item2 + 160);
            await Task.Delay(800);
            notes.Add("after the shown tab: " + Screenshot("tabdrag-shown-after"));
            var order2 = Tabs().Select(t => t.Current.Name).ToList();
            notes.Add("tabs after: " + string.Join(" | ", order2));
            Check(order2.Count == 3 && order2[0].Contains(last), $"the tab shown is now the first ({string.Join(" | ", order2)})");
            var shownAfter = Look(TabOf(last));
            var otherAfter = Look(TabOf(middle));
            notes.Add($"shown tab after: {Show(shownAfter)}; other {Show(otherAfter)}");
            Check(Near(shownAfter.back, shownBefore.back) && Near(shownAfter.text, shownBefore.text),
                $"the tab shown, dragged, looks as it did ({Show(shownAfter)} against {Show(shownBefore)})");
            Check(Near(otherAfter.back, otherBefore.back) && Near(otherAfter.text, otherBefore.text),
                $"and the other tabs as they did ({Show(otherAfter)} against {Show(otherBefore)})");
        }
        finally
        {
            foreach (var id in ids) try { await c.Call("document.close", new { documentId = id }); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "AlwaysShowTabBar", value = false }); } catch { }
        }
    }
}
