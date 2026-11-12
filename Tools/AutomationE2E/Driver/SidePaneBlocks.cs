using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using Newtonsoft.Json.Linq;

/// <summary>
/// PS02: the side pane shows the files above and the outline below (#46, 2026-11-12), not one at a time behind two tabs.
/// The bar between them dragged with the mouse moves the line between the blocks; a block's title row clicked folds it to
/// that row and opens it again; both kept when the pane is closed and opened. A plain text without headings folds the
/// outline by itself (not the reader's setting), and a click on its title opens it all the same.
/// </summary>
internal static partial class Program
{
    private static async Task PS02(List<string> notes)
    {
        using var c = await Session("e2e PS02");
        // A folder of its own: in the shared fixtures folder the file was listed below what the files block shows.
        var folder = Path.Combine(fixtures, "ps02");
        Directory.CreateDirectory(folder);
        var mdPath = Path.Combine(folder, "ps02.md");
        var txtPath = Path.Combine(folder, "ps02-plain.txt");
        File.WriteAllText(mdPath, "# PS02 heading\n\nText under it.\n\n## PS02 second\n\nMore.\n");
        File.WriteAllText(txtPath, "A plain line.\n\nAnother one.\n");
        var md = await Open(c, mdPath);
        var txt = await Open(c, txtPath);
        var windowId = await WindowIdOf(c, md);
        var window = await WindowOf(windowId, c);
        async Task<JToken> Setting(string name) => (await c.Call("test.settings.get", new { windowId, name }))["value"]!;
        Task Set(string name, JToken value) => c.Call("test.settings.set", new { windowId, name, value });
        AutomationElement? ById(string id) =>
            Deep.First(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.AutomationIdProperty, id));
        System.Windows.Rect Rect(string id) => ById(id)?.Current.BoundingRectangle ?? System.Windows.Rect.Empty;
        string Status(string id) => ById(id)?.Current.ItemStatus ?? "(none)";
        // A row of the pane by its name, drawn in the pane (not a row the tree keeps off screen).
        bool InPane(string name)
        {
            var pane = Rect("NavigationView");
            return !pane.IsEmpty && Deep.All(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.NameProperty, name))
                .Cast<AutomationElement>().Any(e => { var r = e.Current.BoundingRectangle; return r.Width > 0 && r.Height > 0 && pane.Contains(new System.Windows.Point(r.Left + r.Width / 2, r.Top + r.Height / 2)); });
        }
        var before = new Dictionary<string, JToken>();
        foreach (var name in new[] { "SidePaneSplit", "SidePaneFilesCollapsed", "SidePaneOutlineCollapsed" }) before[name] = await Setting(name);
        try
        {
            await Set("SidePaneSplit", 0.5);
            await Set("SidePaneFilesCollapsed", false);
            await Set("SidePaneOutlineCollapsed", false);
            await c.Call("window.setView", new { windowId, bounds = new { x = 40, y = 40, width = 1200, height = 820 }, mode = "visual", sidePane = new { open = true, page = "files" } });
            await c.Call("test.folder.open", new { windowId, path = folder });
            await c.Call("document.focus", new { documentId = md });
            await Task.Delay(1500);
            Screenshot("PS02-both");

            // Both blocks at once: the file in the tree above, the heading in the outline below.
            var pane = Rect("NavigationView");
            var files = Rect("Folder");
            var outline = Rect("Toc");
            notes.Add($"pane {pane}; files title {files}; outline title {outline}; {Status("Folder")}/{Status("Toc")}");
            Check(!pane.IsEmpty && !files.IsEmpty && !outline.IsEmpty, "the pane has a files title and an outline title");
            Check(outline.Top > files.Bottom + 60, "the outline is below the files");
            Check(await Eventually(() => Task.FromResult(InPane("ps02.md") && InPane("PS02 heading")), 5000, 200),
                "the file is listed and the heading is listed, both in the pane at once");
            Check(Status("Folder") == "expanded" && Status("Toc") == "expanded", "both say they are open");

            // The bar between them, dragged up 120 pixels with the mouse: the outline's title comes up with it.
            var x = (int)(pane.Left + pane.Width / 2);
            var barY = (int)outline.Top - 4;
            await Drag(window, (x, barY), (x, barY - 120));
            await Task.Delay(600);
            var moved = Rect("Toc");
            var split = (double)await Setting("SidePaneSplit");
            notes.Add($"dragged: outline title {outline.Top} -> {moved.Top}; split 0.5 -> {split:0.000}");
            Check(Math.Abs(outline.Top - 120 - moved.Top) < 25, $"the line between the blocks follows the drag ({outline.Top} -> {moved.Top})");
            Check(split < 0.45, $"and the share is kept ({split:0.000})");

            // The outline's title clicked: folded to its row at the foot of the pane, the heading gone; clicked again, back.
            await Click(window, ById("Toc")!);
            await Task.Delay(600);
            var folded = Rect("Toc");
            notes.Add($"outline folded: title {folded}, pane bottom {pane.Bottom}");
            Check((bool)await Setting("SidePaneOutlineCollapsed") && Status("Toc") == "collapsed", "a click on the outline's title folds it (and that is remembered)");
            Check(pane.Bottom - folded.Bottom < 12 && !InPane("PS02 heading") && InPane("ps02.md"), "folded to its row at the foot of the pane; the files take the rest");

            // The files folded too, the pane closed and opened: both stay folded, the two titles at the top.
            await Click(window, ById("Folder")!);
            await Task.Delay(600);
            Check((bool)await Setting("SidePaneFilesCollapsed") && !InPane("ps02.md"), "a click on the files' title folds them");
            await c.Call("window.setView", new { windowId, sidePane = new { open = false } });
            await Task.Delay(600);
            await c.Call("window.setView", new { windowId, sidePane = new { open = true } });
            await Task.Delay(1200);
            pane = Rect("NavigationView");
            var top = Rect("Toc");
            notes.Add($"reopened: pane {pane}; files {Rect("Folder")}; outline {top}; {Status("Folder")}/{Status("Toc")}");
            Check(Status("Folder") == "collapsed" && Status("Toc") == "collapsed", "closed and opened, the pane keeps both folded");
            Check(top.Top - pane.Top < 90, "the two titles at the top of the pane");

            // Opened again by their titles: the share dragged to is still there.
            await Click(window, ById("Folder")!);
            await Click(window, ById("Toc")!);
            await Task.Delay(800);
            pane = Rect("NavigationView");
            var shared = Rect("Toc");
            notes.Add($"both opened again: outline title {shared}, pane {pane}");
            Check(InPane("ps02.md") && InPane("PS02 heading"), "both open again");
            Check(Math.Abs(shared.Top - moved.Top) < 25, $"at the share dragged to ({moved.Top} then, {shared.Top} now)");

            // A plain text without headings: the outline folds by itself, the setting untouched; its title opens it.
            await c.Call("document.focus", new { documentId = txt });
            Check(await Eventually(() => Task.FromResult(Status("Toc") == "collapsed"), 5000, 200), "a plain text without headings folds the outline");
            Check(!(bool)await Setting("SidePaneOutlineCollapsed"), "by itself: the reader's setting is still open");
            await Click(window, ById("Toc")!);
            await Task.Delay(600);
            Check(Status("Toc") == "expanded", "a click on its title opens it all the same");
            await c.Call("document.focus", new { documentId = md });
            Check(await Eventually(() => Task.FromResult(Status("Toc") == "expanded" && InPane("PS02 heading")), 5000, 200), "back to the .md: the outline open with its headings");
            Screenshot("PS02-end");
        }
        finally
        {
            foreach (var (name, value) in before)
            {
                try { await Set(name, value); } catch { }
            }
            foreach (var id in new[] { txt, md })
            {
                try { await c.Call("document.close", new { documentId = id }); } catch { }
            }
        }
    }
}
