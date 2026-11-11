using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using Newtonsoft.Json.Linq;

/// <summary>
/// TM01: a tab's right-click menu closes others (M05 of the 2026-11-11 menu report). Four documents open: Close Tabs to the
/// Right on the second leaves the first two, the second shown; Close Others on the first leaves it alone, shown. The
/// file entries (Copy as Path, Open File Location, Show in Side Bar) are offered for a file's tab.
/// </summary>
internal static partial class Program
{
    private static async Task TM01(List<string> notes)
    {
        var folder = FixtureFolder("tm01");
        var paths = Enumerable.Range(1, 4).Select(i => Path.Combine(folder, $"tm01-{i}.md")).ToList();
        for (var i = 0; i < paths.Count; i++) File.WriteAllText(paths[i], $"# tm01 document {i + 1}\n");
        using var c = await Session("e2e TM01");
        var ids = new List<string>();
        foreach (var path in paths) ids.Add(await Open(c, path));
        var windowId = await WindowIdOf(c, ids[0]);
        var window = await WindowOf(windowId, c);
        await c.Call("test.settings.set", new { windowId, name = "AlwaysShowTabBar", value = true });
        List<AutomationElement> Tabs() => Deep.All(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem)).Cast<AutomationElement>().ToList();
        List<string> Names() => Tabs().Select(t => t.Current.Name).Where(n => n.Contains("tm01-")).ToList();
        async Task<JArray> Documents() => (JArray)(await c.Call("document.list", new { windowId }))["documents"]!;
        async Task<string?> Shown() => (string?)(await Documents()).FirstOrDefault(d => (bool?)d["active"] == true)?["path"];
        async Task Menu(string name, string entry)
        {
            AutomationElement? tab = null;
            await Eventually(() => Task.FromResult((tab = Tabs().FirstOrDefault(t => t.Current.Name.Contains(name))) != null), 5000);
            Check(tab != null, $"the tab of {name} is in the strip");
            await Click(window, tab!, Mouse.Right);
            await Task.Delay(600);
            var item = await MenuEntry(window, entry);
            Check(item != null, $"{name}'s tab menu offers {entry}");
            await ClickInPopup(window, item!);
        }
        try
        {
            await Eventually(() => Task.FromResult(Names().Count == 4), 8000);
            var order = Names();
            notes.Add("tabs: " + string.Join(" | ", order));
            Check(order.Count == 4, "four documents in four tabs");
            // The strip's order, whatever order they were opened in.
            var names = paths.Select(p => Path.GetFileNameWithoutExtension(p)!).ToList();
            var first = names.First(n => order[0].Contains(n));
            var second = names.First(n => order[1].Contains(n));

            // The file entries, on a file's tab.
            AutomationElement? tab = Tabs().First(t => t.Current.Name.Contains(second));
            await Click(window, tab, Mouse.Right);
            await Task.Delay(600);
            foreach (var entry in new[] { "TabCopyPathItem", "TabOpenLocationItem", "TabShowInSideBarItem", "TabCloseItem" })
            {
                var item = await MenuEntry(window, entry);
                Check(item != null && item.Current.IsEnabled, $"a file's tab menu offers {entry}");
            }
            await CloseMenus();

            await Menu(second, "TabCloseToTheRightItem");
            await Eventually(() => Task.FromResult(Names().Count == 2), 8000);
            var left = Names();
            notes.Add("after Close Tabs to the Right: " + string.Join(" | ", left));
            Check(left.Count == 2 && left[0].Contains(first) && left[1].Contains(second), "Close Tabs to the Right on the second leaves the first two");
            var shown = await Shown();
            Check(shown != null && Path.GetFileNameWithoutExtension(shown) == second, $"the tab the menu was opened on is shown ({shown})");

            await Menu(first, "TabCloseOthersItem");
            await Eventually(() => Task.FromResult(Names().Count == 1), 8000);
            left = Names();
            notes.Add("after Close Others: " + string.Join(" | ", left));
            Check(left.Count == 1 && left[0].Contains(first), "Close Others on the first leaves it alone");
            shown = await Shown();
            Check(shown != null && Path.GetFileNameWithoutExtension(shown) == first, $"and shows it ({shown})");
            Check((await Documents()).Count == 1, "the window holds one document");
        }
        finally
        {
            await CloseMenus();
            foreach (var d in await Documents()) try { await c.Call("document.close", new { documentId = (string)d["documentId"]! }); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "AlwaysShowTabBar", value = false }); } catch { }
        }
    }
}
