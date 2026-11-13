using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;

/// <summary>
/// XM06 (the user, 2026-11-13; the menu report's M12): a greyed menu item says why.
/// - a document not saved yet: its tab's menu says so above its file items, which are off;
/// - the favourites full: a file's "Add to favourites" says the list is full and how many it holds;
/// - unsaved changes: the status bar's encoding menu says to save first, by Reopen with encoding.
/// </summary>
internal static partial class Program
{
    private static async Task XM06(List<string> notes)
    {
        var folder = FixtureFolder("xm06");
        var path = Path.Combine(folder, "xm06.md");
        File.WriteAllText(path, "# XM06\n\nSome words here.\n");
        using var c = await Session("e2e XM06");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        string? untitled = null;
        await c.Call("test.settings.set", new { windowId, name = "AlwaysShowTabBar", value = true });
        // Wide, the status bar on and its encoding item shown (hidden by default), not squeezed into "...".
        var hidden = (string?)(await c.Call("test.settings.get", new { windowId, name = "StatusBarHiddenItems" }))["value"] ?? "format";
        await c.Call("test.settings.set", new { windowId, name = "StatusBarHiddenItems", value = "" });
        await c.Call("window.setView", new { windowId, statusBar = true, bounds = new { x = 40, y = 40, width = 1400, height = 800 } });
        List<AutomationElement> Tabs() => Deep.All(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem)).Cast<AutomationElement>().ToList();
        async Task RightClickTab(Func<AutomationElement, bool> which, string what)
        {
            AutomationElement? tab = null;
            await Eventually(() => Task.FromResult((tab = Tabs().FirstOrDefault(which)) != null), 5000);
            if (tab == null) throw new CaseFailed($"no tab for {what} ({string.Join(", ", Tabs().Select(t => t.Current.Name))})");
            await Click(window, tab, Mouse.Right);
            await Task.Delay(700);
        }
        try
        {
            // A document not saved yet.
            untitled = (string)(await c.Call("document.create", new { text = "# not saved\n", reveal = "document" }))["documentId"]!;
            await Task.Delay(800);
            // Its tab: the one shown (other cases may leave tabs of their own; its name is the interface's word).
            await RightClickTab(t => t.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var s) && ((SelectionItemPattern)s).Current.IsSelected, "the new document");
            var why = FindAnywhereInHost(window, "TabNotSavedYetItem");
            var copy = FindAnywhereInHost(window, "TabCopyPathItem");
            notes.Add($"untitled tab: '{why?.Current.Name}' shown {why != null && !why.Current.IsOffscreen}, copy path enabled {copy?.Current.IsEnabled}");
            Check(why != null && !why.Current.IsOffscreen && why.Current.Name.Length > 0 && copy?.Current.IsEnabled == false, "a document not saved yet: its tab's menu says why its file items are off");
            await CloseMenus();

            // The favourites full (50).
            await c.Call("test.favorites.clear");
            var many = Enumerable.Range(1, 50).Select(i => { var p = Path.Combine(folder, $"fav{i:00}.md"); File.WriteAllText(p, "x"); return p; }).ToArray();
            var filled = await c.Call("test.favorites.add", new { paths = many });
            notes.Add($"favourites: {filled}");
            await RightClickTab(t => t.Current.Name.Contains("xm06"), "xm06.md");
            var favorite = FindAnywhereInHost(window, "TabFavoriteItem");
            notes.Add($"favourite item with the list full: '{favorite?.Current.Name}', enabled {favorite?.Current.IsEnabled}");
            Check(favorite != null && favorite.Current.Name.Contains("50") && !favorite.Current.IsEnabled, "the favourites full: the item says so, with how many it holds");
            await CloseMenus();
            await c.Call("test.favorites.clear");

            // Unsaved changes: Reopen with encoding says to save first.
            // Its tab clicked, as a person goes back to it.
            AutomationElement? own = null;
            await Eventually(() => Task.FromResult((own = Tabs().FirstOrDefault(t => t.Current.Name.Contains("xm06"))) != null), 5000);
            await Click(window, own ?? throw new CaseFailed("no tab for xm06.md"));
            await Task.Delay(800);
            await ClickEditorText(c, windowId, window, "Some words");
            TypeChar('Q');
            await Eventually(async () => !(bool)(await Get(c, id))["saved"]!, 5000, 200);
            await Click(window, FindAnywhereInHost(window, "StatusBarFileFormat") ?? throw new CaseFailed("no format item"));
            await Task.Delay(700);
            var reopen = FindAnywhereInHost(window, "ReopenWithEncoding");
            var reason = FindAnywhereInHost(window, "ReopenWithEncodingWhyOff");
            notes.Add($"encoding menu, unsaved: reopen enabled {reopen?.Current.IsEnabled}, '{reason?.Current.Name}'");
            Check(reopen?.Current.IsEnabled == false && reason != null && reason.Current.Name.Length > 0, "unsaved changes: the encoding menu says why Reopen with encoding is off");
        }
        finally
        {
            await CloseMenus();
            try { await c.Call("test.favorites.clear"); } catch { }
            try { await c.Call("document.save", new { documentId = id }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
            if (untitled != null) try { await c.Call("document.close", new { documentId = untitled }); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "AlwaysShowTabBar", value = false }); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "StatusBarHiddenItems", value = hidden }); } catch { }
        }
    }
}
