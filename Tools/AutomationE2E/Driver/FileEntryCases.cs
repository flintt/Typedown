using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using Newtonsoft.Json.Linq;

/// <summary>
/// FE01: a file's Open File Location and Copy as Path the same from its tab, its row in the folder tree, its File > Open
/// Recent entry and its home page entry - all four menus clicked with a real mouse (they each did it their own way: the
/// modularity audit's M05, 2026-11-12). Then a recent file that is gone: its place is the folder it was in.
/// </summary>
internal static partial class Program
{
    private static async Task FE01(List<string> notes)
    {
        var folder = FixtureFolder("fe01");
        var file = Path.Combine(folder, "fe01-entry.md");
        File.WriteAllText(file, "# fe01 entry\n");
        var gone = Path.Combine(folder, "fe01-gone.md");
        File.WriteAllText(gone, "# fe01 gone\n");
        using var c = await Session("e2e FE01");
        var id = await Open(c, file);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        string? blank = null;
        await c.Call("test.settings.set", new { windowId, name = "AlwaysShowTabBar", value = true });
        async Task<List<string>> Locations() =>
            ((JArray)(await c.Call("test.links.opened"))["links"]!).Select(x => (string)x!).Where(x => x.StartsWith("location:")).Select(x => x["location:".Length..]).ToList();
        var results = new List<string>();
        // One menu: its Open File Location (the place asked of Explorer) and its Copy as Path (the clipboard after).
        async Task Both(string where, Func<Task> openMenu, string locationId, string copyId)
        {
            var before = (await Locations()).Count;
            await openMenu();
            await ClickInPopup(window, await MenuEntry(window, locationId));
            await Task.Delay(600);
            var after = await Locations();
            var shown = after.Count > before ? after.Last() : null;
            await CloseMenus();
            SetClipboardText("fe01 nothing yet");
            await openMenu();
            await ClickInPopup(window, await MenuEntry(window, copyId));
            await Task.Delay(600);
            var copied = ClipboardText();
            await CloseMenus();
            notes.Add($"{where}: shown {shown ?? "nothing"}, copied {copied}");
            if (!SamePath(shown, file)) results.Add($"{where}: shown {shown ?? "nothing"}");
            if (copied != file) results.Add($"{where}: copied \"{copied}\"");
        }
        try
        {
            await c.Call("test.folder.open", new { windowId, path = folder });
            await Task.Delay(1000);

            // Its tab.
            List<AutomationElement> Tabs() => Deep.All(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem)).Cast<AutomationElement>().ToList();
            await Both("tab", async () =>
            {
                AutomationElement? tab = null;
                await Eventually(() => Task.FromResult((tab = Tabs().FirstOrDefault(t => t.Current.Name.Contains("fe01-entry"))) != null), 5000);
                await Click(window, tab!, Mouse.Right);
                await Task.Delay(600);
            }, "TabOpenLocationItem", "TabCopyPathItem");

            // Its row in the folder tree.
            await Both("folder tree", async () =>
            {
                AutomationElement? row = null;
                await Eventually(() => Task.FromResult((row = TreeRow(window, "fe01-entry.md")) != null), 5000);
                await Click(window, row!, Mouse.Right);
                await Task.Delay(600);
            }, "TreeLocationItem", "TreeCopyPathItem");

            // Its Open Recent entry (the newest).
            await Both("Open Recent", async () =>
            {
                var entry = await MenuEntry(window, "MenuBarFileItem", "OpenRecentSubMenu", "RecentFile-0");
                Check(SamePath(entry.Current.HelpText, file), $"the newest recent entry is the file ({entry.Current.HelpText})");
                await Click(window, entry, Mouse.Right);
                await Task.Delay(600);
            }, "RecentLocationItem", "RecentCopyPathItem");

            // Its home page entry.
            await ClickInPopup(window, await MenuEntry(window, "MenuBarFileItem", "HomeMenuItem"));
            Check(await HomeShown(c, windowId, true), "File > Home shows the home page");
            blank = await ActiveOf(c, windowId);
            await Both("home page", async () =>
            {
                // In the home page only (a search of the whole window found the file's tab by its name, and right-clicked it).
                AutomationElement? row = null;
                AutomationElement? Find()
                {
                    var home = FindIn(window, "HomePage");
                    if (home == null) return null;
                    // The path as the list has it (its case may not be the fixture's), or the name.
                    return home.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>()
                        .FirstOrDefault(x => SamePath(x.Current.HelpText, file) || x.Current.Name.Contains("fe01-entry", StringComparison.OrdinalIgnoreCase));
                }
                await Eventually(() => Task.FromResult((row = Find()) != null), 5000);
                if (row == null)
                {
                    var home = FindIn(window, "HomePage");
                    var seen = home == null ? "no home page" : string.Join(" | ", home.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>()
                        .Select(x => $"{x.Current.ControlType.ProgrammaticName.Replace("ControlType.", "")}:{x.Current.Name}{(string.IsNullOrEmpty(x.Current.HelpText) ? "" : " [" + x.Current.HelpText + "]")}").Take(40));
                    notes.Add("home page shows: " + seen);
                    notes.Add("home state: " + (await HomeState(c, windowId)).ToString(Newtonsoft.Json.Formatting.None));
                    throw new CaseFailed("the file is not in the home page's lists");
                }
                notes.Add($"home page entry: {row.Current.ControlType.ProgrammaticName} '{row.Current.Name}'");
                await Click(window, row, Mouse.Right);
                await Task.Delay(600);
            }, "HomeLocationItem", "HomeCopyPathItem");

            Check(results.Count == 0, "Open File Location and Copy as Path give the file in all four menus" + (results.Count == 0 ? "" : ": " + string.Join("; ", results)));

            // A recent file that is gone: its place is its folder.
            var goneId = await Open(c, gone);
            await c.Call("document.close", new { documentId = goneId });
            File.Delete(gone);
            await Task.Delay(500);
            var before = (await Locations()).Count;
            var goneEntry = await MenuEntry(window, "MenuBarFileItem", "OpenRecentSubMenu", "RecentFile-0");
            Check(SamePath(goneEntry.Current.HelpText, gone), $"the newest recent entry is the file now gone ({goneEntry.Current.HelpText})");
            await Click(window, goneEntry, Mouse.Right);
            await Task.Delay(600);
            await ClickInPopup(window, await MenuEntry(window, "RecentLocationItem"));
            await Task.Delay(600);
            var after = await Locations();
            var shownGone = after.Count > before ? after.Last() : null;
            notes.Add($"gone file: shown {shownGone ?? "nothing"}");
            Check(SamePath(shownGone, folder), $"a recent file that is gone: its folder is shown ({shownGone ?? "nothing"})");
        }
        finally
        {
            await CloseMenus();
            if (blank != null) try { await c.Call("document.close", new { documentId = blank }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "AlwaysShowTabBar", value = false }); } catch { }
        }
    }
}
