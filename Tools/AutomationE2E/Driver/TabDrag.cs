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
    /// <summary>
    /// TD02: a tab dragged out of the strip and let go over the document, lower in it, over the status bar and over the
    /// title bar opens no dialog and adds or loses no tab (the user, 2026-11-11 and 12: an open dialog came up now and
    /// then as a tab was dragged). Each place dragged to twice, slowly and quickly.
    /// </summary>
    private static async Task TD02(List<string> notes)
    {
        var folder = FixtureFolder("td02");
        var paths = Enumerable.Range(1, 2).Select(i => Path.Combine(folder, $"td02-{i}.md")).ToList();
        for (var i = 0; i < paths.Count; i++) File.WriteAllText(paths[i], $"# td02 document {i + 1}\n\nSome text.\n");
        using var c = await Session("e2e TD02");
        var ids = new List<string>();
        foreach (var path in paths) ids.Add(await Open(c, path));
        var windowId = await WindowIdOf(c, ids[0]);
        var window = await WindowOf(windowId, c);
        await c.Call("test.settings.set", new { windowId, name = "AlwaysShowTabBar", value = true });
        List<AutomationElement> Tabs() => Deep.All(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem)).Cast<AutomationElement>()
            .Where(t => t.Current.Name.Contains("td02-")).ToList();
        // The host's windows other than its main one: a dialog (#32770, the file pickers' class) or any window come up.
        List<string> Others() => AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, hostPid)).Cast<AutomationElement>()
            .Where(w => new IntPtr(w.Current.NativeWindowHandle) != window && !w.Current.IsOffscreen && w.Current.BoundingRectangle.Width > 50)
            .Select(w => $"{w.Current.ClassName} '{w.Current.Name}'").ToList();
        try
        {
            await Eventually(() => Task.FromResult(Tabs().Count == 2), 8000);
            var before = Others();
            notes.Add("other windows before: " + (before.Count == 0 ? "none" : string.Join(", ", before)));
            var bounds = AutomationElement.FromHandle(window).Current.BoundingRectangle;
            var places = new (string name, int x, int y)[]
            {
                ("the document", (int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + bounds.Height / 3)),
                ("low in the document", (int)(bounds.Left + bounds.Width / 2), (int)(bounds.Bottom - 80)),
                ("the status bar", (int)(bounds.Left + bounds.Width / 2), (int)(bounds.Bottom - 12)),
                ("the left of the document", (int)(bounds.Left + 60), (int)(bounds.Top + bounds.Height / 2)),
                ("the title bar", (int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + 14)),
            };
            var found = new List<string>();
            foreach (var place in places)
                foreach (var steps in new[] { 20, 5 })
                {
                    var tab = Tabs().First();
                    await Drag(window, CenterOf(tab), (place.x, place.y), steps: steps);
                    await Task.Delay(1200);
                    var others = Others().Except(before).ToList();
                    var count = Tabs().Count;
                    if (others.Count > 0 || count != 2)
                    {
                        found.Add($"{place.name} ({steps} steps): {string.Join(", ", others)}; {count} tabs");
                        notes.Add($"let go over {place.name} ({steps} steps): " + Screenshot($"td02-{found.Count}"));
                        for (var i = 0; i < 2; i++) { Send(Key(0x1B, false), Key(0x1B, true)); await Task.Delay(400); }
                    }
                }
            notes.Add(found.Count == 0 ? "nothing came up" : "came up: " + string.Join("; ", found));
            Check(found.Count == 0, $"a tab let go outside the strip opens nothing and keeps the tabs ({string.Join("; ", found)})");
        }
        finally
        {
            try
            {
                foreach (var d in ((Newtonsoft.Json.Linq.JArray)(await c.Call("document.list", new { windowId }))["documents"]!).Select(d => (string)d["documentId"]!))
                    if (!ids.Contains(d)) ids.Add(d);
            }
            catch { }
            foreach (var id in ids) try { await c.Call("document.close", new { documentId = id }); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "AlwaysShowTabBar", value = false }); } catch { }
        }
    }

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
        // A tab's edge: the colours of a ring 2 px inside its bounds, no text in it - where the edge the dropped tab kept is
        // drawn (the user, 2026-11-11: an outline like the shown tab's card, without its fill; the colours inside did not
        // change, and the first TD01 passed).
        List<Color> Ring(AutomationElement tab)
        {
            var r = tab.Current.BoundingRectangle;
            var area = new Rectangle((int)r.Left, (int)r.Top, (int)r.Width, (int)r.Height);
            using var bitmap = new Bitmap(area.Width, area.Height);
            using (var g = Graphics.FromImage(bitmap)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
            var ring = new List<Color>();
            for (var x = 6; x < area.Width - 6; x += 2) { ring.Add(bitmap.GetPixel(x, 2)); ring.Add(bitmap.GetPixel(x, area.Height - 3)); }
            for (var y = 6; y < area.Height - 6; y += 2) { ring.Add(bitmap.GetPixel(2, y)); ring.Add(bitmap.GetPixel(area.Width - 3, y)); }
            return ring;
        }
        // The shadow a tab left lifted keeps: pixels of its outer band (0-2 px in) darker than its own background - a ring two
        // or three steps darker than the strip, which the colours inside and a ring further in did not catch.
        int DarkEdge(AutomationElement tab)
        {
            var r = tab.Current.BoundingRectangle;
            var area = new Rectangle((int)r.Left - 1, (int)r.Top - 1, (int)r.Width + 2, (int)r.Height + 2);
            using var bitmap = new Bitmap(area.Width, area.Height);
            using (var g = Graphics.FromImage(bitmap)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
            var back = bitmap.GetPixel(area.Width / 2, area.Height / 2 + area.Height / 4);
            var dark = 0;
            for (var d = 0; d <= 3; d++)
            {
                for (var x = 8; x < area.Width - 8; x++)
                    foreach (var y in new[] { d, area.Height - 1 - d })
                        if (bitmap.GetPixel(x, y).R + 2 < back.R) dark++;
                for (var y = 8; y < area.Height - 8; y++)
                    foreach (var x in new[] { d, area.Width - 1 - d })
                        if (bitmap.GetPixel(x, y).R + 2 < back.R) dark++;
            }
            return dark;
        }
        // How far two rings are apart: the mean colour distance, point by point (as many points: same-sized tabs).
        double Apart(List<Color> a, List<Color> b)
        {
            var n = Math.Min(a.Count, b.Count);
            if (n == 0) return 0;
            double sum = 0;
            for (var i = 0; i < n; i++) sum += Math.Abs(a[i].R - b[i].R) + Math.Abs(a[i].G - b[i].G) + Math.Abs(a[i].B - b[i].B);
            return sum / n;
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
            // How far the tab shown reaches over the one before it, as drawn by itself (its rounded foot, outside its box):
            // what a new tab shown may too.
            var shownOver = Math.Max(0, TabOf(middle).Current.BoundingRectangle.Right - TabOf(last).Current.BoundingRectangle.Left);
            notes.Add($"the tab shown reaches {shownOver:0} px over the one before it");
            var before = (moved: Look(TabOf(first)), other: Look(TabOf(middle)));
            notes.Add($"before: {first} {Show(before.moved)}; {middle} {Show(before.other)}");
            var ringBefore = Ring(TabOf(first));
            var ringOtherBefore = Ring(TabOf(middle));
            notes.Add($"edges before: {first} against {middle} {Apart(ringBefore, ringOtherBefore):F1}");

            // Dragged past the last, with a picture while it is held.
            var from = CenterOf(TabOf(first));
            var lastRect = TabOf(last).Current.BoundingRectangle;
            var to = ((int)(lastRect.Right - 6), from.Item2);
            // The + after the tabs stays where it is while a tab is held: it went right by a tab's width as the drag began,
            // with no tab added (the user, 2026-11-11).
            var addBefore = FindIn(window, "AddButton")?.Current.BoundingRectangle;
            System.Windows.Rect? addHeld = null;
            await Drag(window, from, to, steps: 20, whileHeld: async () =>
            {
                await Task.Delay(300);
                addHeld = FindIn(window, "AddButton")?.Current.BoundingRectangle;
                notes.Add("held: " + Screenshot("tabdrag-held"));
            });
            notes.Add($"+ before {addBefore}, while held {addHeld}");
            Check(addBefore != null && addHeld != null && Math.Abs(addHeld.Value.Left - addBefore.Value.Left) < 4,
                $"the + stays where it was while a tab is dragged ({addBefore?.Left:0} before, {addHeld?.Left:0} held)");
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
            // Its edge too: as before the drag, and as the other tab not shown has it.
            var ringAfter = Ring(TabOf(first));
            var ringOtherAfter = Ring(TabOf(middle));
            var self = Apart(ringAfter, ringBefore);
            var other = Apart(ringAfter, ringOtherAfter);
            notes.Add($"edges after: {first} against itself before {self:F1}, against {middle} {other:F1}");
            var darkMoved = DarkEdge(TabOf(first));
            var darkOther = DarkEdge(TabOf(middle));
            notes.Add($"dark edge pixels: {first} {darkMoved}, {middle} {darkOther}");
            Check(darkMoved <= darkOther + 10, $"the tab moved has no darker edge (a shadow) the other tab not shown has not ({darkMoved} against {darkOther} pixels)");
            Check(self < 12 && other < 12, $"the tab moved has no edge the others have not (apart {self:F1} from itself before, {other:F1} from {middle})");

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

            // Dragged again at once, twice (within the 700 ms after a move in which the tabs' states are put back): each
            // move taken (the user, 2026-11-12: after a drop no tab could be dragged any more).
            for (var round = 1; round <= 2; round++)
            {
                var was = Tabs().Select(t => t.Current.Name).ToList();
                var head = Tabs().First();
                var end = Tabs().Last().Current.BoundingRectangle;
                var at = CenterOf(head);
                await Drag(window, at, ((int)(end.Right - 6), at.Item2), steps: 12);
                await Task.Delay(round == 1 ? 150 : 600);
                var now2 = Tabs().Select(t => t.Current.Name).ToList();
                notes.Add($"drag again {round}: {string.Join(" | ", now2)}");
                Check(now2.Count == 3 && now2[2] == was[0], $"a tab dragged again right after a drop moves ({round}: {string.Join(" | ", was)} -> {string.Join(" | ", now2)})");
            }

            // A new tab after the drags: beside the others, not over them, and the + after it (the user, 2026-11-12: a
            // new tab went over others once the strip was held to the tabs' width).
            SetCursorPos(editor.Item1, editor.Item2 + 160);
            await Task.Delay(400);
            var addButton = FindIn(window, "AddButton");
            Check(addButton != null, "the + is there");
            if (addButton != null)
            {
                List<(string name, System.Windows.Rect r)> AllTabs() => Deep.All(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem)).Cast<AutomationElement>()
                    .Select(t => (name: t.Current.Name, r: t.Current.BoundingRectangle)).Where(t => !t.r.IsEmpty && t.r.Width > 0).OrderBy(t => t.r.Left).ToList();
                var countBefore = AllTabs().Count;
                Invoke(addButton);
                await Task.Delay(1500);
                var all = AllTabs();
                notes.Add("after +: " + string.Join(" | ", all.Select(t => $"{t.name} {t.r.Left:0}-{t.r.Right:0}")));
                var overlaps = all.Zip(all.Skip(1), (a, b) => (a, b)).Where(p => p.b.r.Left < p.a.r.Right - shownOver - 2).Select(p => $"{p.a.name} / {p.b.name} {p.a.r.Right - p.b.r.Left:0} px").ToList();
                Check(all.Count == countBefore + 1 && overlaps.Count == 0, $"one tab more, side by side, none over another further than the tab shown reaches ({countBefore} -> {all.Count} tabs; over: {string.Join(", ", overlaps)})");
                var plus = FindIn(window, "AddButton")?.Current.BoundingRectangle;
                notes.Add($"+ at {plus?.Left:0}, the last tab ends at {all.LastOrDefault().r.Right:0}");
                Check(plus != null && all.Count > 0 && plus.Value.Left >= all.Last().r.Right - 2 && plus.Value.Left <= all.Last().r.Right + 40,
                    $"the + right after the last tab ({plus?.Left:0} against {all.LastOrDefault().r.Right:0})");
            }
        }
        finally
        {
            try
            {
                foreach (var d in ((Newtonsoft.Json.Linq.JArray)(await c.Call("document.list", new { windowId }))["documents"]!).Select(d => (string)d["documentId"]!))
                    if (!ids.Contains(d)) ids.Add(d);
            }
            catch { }
            foreach (var id in ids) try { await c.Call("document.close", new { documentId = id }); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "AlwaysShowTabBar", value = false }); } catch { }
        }
    }
}
