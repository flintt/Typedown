using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Recent files and folders, the folder pane's navigation, favourites and the home page (docs/todo.md, "最近、收藏、文件夹导航与首页").
/// Menus and buttons are used as a person uses them (UI Automation by their automation ids, real clicks where a popup
/// needs one); what the lists hold is read back through the test host.
/// </summary>
internal static partial class Program
{
    // ---- helpers ----

    private static async Task<string> FirstWindowId(Client c) =>
        (string)((JArray)(await c.Call("window.list"))["windows"]!)[0]["windowId"]!;

    private sealed record RecentEntry(string Kind, string Text, string Path, bool Missing);

    /// <summary>File > Open recent as the window builds it (test.recent.menu), each path looked at afresh.</summary>
    private static async Task<List<RecentEntry>> RecentMenu(Client c, string windowId) =>
        ((JArray)(await c.Call("test.recent.menu", new { windowId }))["entries"]!)
            .Select(e => new RecentEntry((string)e["kind"]!, (string)e["text"]!, (string)e["path"]!, (bool)e["missing"]!)).ToList();

    private static string Show(IEnumerable<RecentEntry> entries) =>
        string.Join(", ", entries.Select(e => $"{e.Kind}:{e.Text}{(e.Missing ? " (missing)" : "")}"));

    private static bool SamePath(string? a, string? b) =>
        a != null && b != null && string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>A folder under the fixtures with one Markdown file in it.</summary>
    private static string FixtureFolder(string name)
    {
        var folder = Path.Combine(fixtures, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name + ".md"), "# " + name + "\n");
        return folder;
    }

    /// <summary>The folder pane's state (test.folder.state): the root it shows and the window's work folder.</summary>
    private static async Task<JToken> FolderState(Client c, string windowId) => await c.Call("test.folder.state", new { windowId });

    /// <summary>
    /// A menu entry reached through its parents by their automation ids, as a person opens them: the menu bar's menu
    /// expanded, each submenu expanded, the last one returned (not invoked). Fails naming the step that was not there.
    /// </summary>
    private static async Task<AutomationElement> MenuEntry(IntPtr window, params string[] ids)
    {
        await Activate(window);
        AutomationElement? item = null;
        for (var i = 0; i < ids.Length; i++)
        {
            var id = ids[i];
            if (!await Eventually(() => Task.FromResult((item = FindAnywhereInHost(window, id)) != null), 4000, 100))
                throw new CaseFailed($"no menu entry '{id}' (after {string.Join(" > ", ids.Take(i))})");
            if (i < ids.Length - 1)
            {
                if (item!.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expand)) ((ExpandCollapsePattern)expand).Expand();
                else await ClickInPopup(window, item);
                await Task.Delay(400);
            }
        }
        return item!;
    }

    /// <summary>An element by automation id in the window or in one of its process's popups (menus are popups of their own).</summary>
    private static AutomationElement? FindAnywhereInHost(IntPtr window, string automationId)
    {
        var condition = new PropertyCondition(AutomationElement.AutomationIdProperty, automationId);
        var inWindow = Deep.First(AutomationElement.FromHandle(window), condition);
        if (inWindow != null && !inWindow.Current.IsOffscreen) return inWindow;
        foreach (AutomationElement top in AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, hostPid)))
        {
            var found = Deep.First(top, condition);
            if (found != null && !found.Current.IsOffscreen) return found;
        }
        return inWindow;
    }

    private static void Invoke(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke)) ((InvokePattern)invoke).Invoke();
        else if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle)) ((TogglePattern)toggle).Toggle();
        else throw new CaseFailed($"'{element.Current.AutomationId}' can be neither invoked nor toggled");
    }

    /// <summary>A real right-click on an element of an open popup (a menu entry), the pointer moved onto it first.</summary>
    private static async Task RightClickInPopup(AutomationElement element)
    {
        var (x, y) = CenterOf(element);
        int screenW = GetSystemMetrics(0), screenH = GetSystemMetrics(1);
        foreach (var dx in new[] { -6, -3, 0 })
        {
            Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dx = (x + dx) * 65535 / (screenW - 1), dy = y * 65535 / (screenH - 1), dwFlags = 0x0001 | 0x8000 } } });
            await Task.Delay(40);
        }
        await Task.Delay(100);
        Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = 0x0008 } } }, new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = 0x0010 } } });
    }

    private static async Task CloseMenus()
    {
        for (var i = 0; i < 3; i++)
        {
            Send(Key(0x1B, false), Key(0x1B, true));
            await Task.Delay(200);
        }
    }

    // ---- recent files and folders ----

    private static async Task RC01(List<string> notes)
    {
        using var c = await Session("e2e RC01");
        var windowId = await FirstWindowId(c);
        var window = await WindowOf(windowId, c);
        var a = FixtureFolder("rc01-A");
        var b = FixtureFolder("rc01-B");
        try
        {
            await c.Call("test.recent.clear", new { windowId, what = "folders" });
            Check((bool)(await c.Call("test.folder.open", new { windowId, path = a }))["opened"]!, "folder A opens");
            Check((bool)(await c.Call("test.folder.open", new { windowId, path = b }))["opened"]!, "folder B opens");
            List<RecentEntry> folders = new();
            await Eventually(async () => (folders = (await RecentMenu(c, windowId)).Where(e => e.Kind == "folder").ToList()).Count >= 2);
            notes.Add("Open recent > Folders: " + Show(folders));
            Check(folders.Count == 2 && SamePath(folders[0].Path, b) && SamePath(folders[1].Path, a), "Open recent > Folders lists B, then A");
            Check(folders[0].Text.StartsWith("rc01-B") && folders[0].Text.Contains(Path.GetFileName(fixtures)), $"an entry shows the folder's name and the folder it is in ({folders[0].Text})");

            // A, chosen in the menu as a person does: the folder pane's root goes there.
            var entry = await MenuEntry(window, "MenuBarFileItem", "OpenRecentSubMenu", "OpenRecentFoldersSubMenu", "RecentFolder-1");
            notes.Add($"RecentFolder-1: '{entry.Current.Name}', help '{entry.Current.HelpText}'");
            Check(SamePath(entry.Current.HelpText, a), "the second folder entry is A (its full path is its help text)");
            await ClickInPopup(window, entry);
            JToken? state = null;
            var moved = await Eventually(async () => SamePath((string?)(state = await FolderState(c, windowId))["root"], a), 5000);
            notes.Add("folder pane: " + state?.ToString(Formatting.None));
            Check(moved, "choosing A in Open recent > Folders makes it the folder pane's root");
        }
        finally
        {
            await CloseMenus();
        }
    }

    private static async Task RC02(List<string> notes)
    {
        using var c = await Session("e2e RC02");
        var windowId = await FirstWindowId(c);
        var window = await WindowOf(windowId, c);
        var gone = Fixture("rc02-gone.md", "# RC02\n");
        var kept = Fixture("rc02-kept.md", "# RC02 kept\n");
        var folder = FixtureFolder("rc02-folder");
        try
        {
            await c.Call("test.recent.clear", new { windowId, what = "all" });
            foreach (var path in new[] { gone, kept })
            {
                var id = await Open(c, path);
                await c.Call("document.close", new { documentId = id });
            }
            await c.Call("test.folder.open", new { windowId, path = folder });
            File.Delete(gone);
            List<RecentEntry> menu = new();
            await Eventually(async () => (menu = await RecentMenu(c, windowId)).Any(e => SamePath(e.Path, gone) && e.Missing), 6000);
            notes.Add("with rc02-gone.md deleted: " + Show(menu));
            Check(menu.Any(e => SamePath(e.Path, gone) && e.Missing), "a deleted recent file stays in the list, greyed out");
            Check(menu.Any(e => SamePath(e.Path, kept) && !e.Missing), "a file still there is not greyed out");

            // Removed with a right-click on its entry, the menu open.
            var index = menu.Where(e => e.Kind == "file").ToList().FindIndex(e => SamePath(e.Path, gone));
            var entry = await MenuEntry(window, "MenuBarFileItem", "OpenRecentSubMenu", "RecentFile-" + index);
            Check(SamePath(entry.Current.HelpText, gone), $"RecentFile-{index} is rc02-gone.md ({entry.Current.HelpText})");
            await RightClickInPopup(entry);
            await Task.Delay(800);
            await CloseMenus();
            var removed = await Eventually(async () => !(menu = await RecentMenu(c, windowId)).Any(e => SamePath(e.Path, gone)), 4000);
            notes.Add("after the right-click: " + Show(menu));
            Check(removed, "a right-click on the entry removes it from the list");

            // Clear recent files: the folders stay.
            var clear = await MenuEntry(window, "MenuBarFileItem", "OpenRecentSubMenu", "ClearRecentFilesItem");
            await ClickInPopup(window, clear);
            await Task.Delay(800);
            var cleared = await Eventually(async () => !(menu = await RecentMenu(c, windowId)).Any(e => e.Kind == "file"), 4000);
            notes.Add("after Clear recent files: " + Show(menu));
            Check(cleared, "Clear recent files empties the files");
            Check(menu.Any(e => e.Kind == "folder" && SamePath(e.Path, folder)), "Clear recent files leaves the recent folders alone");
        }
        finally
        {
            await CloseMenus();
        }
    }

    private static async Task RC03(List<string> notes)
    {
        using var c = await Session("e2e RC03");
        var windowId = await FirstWindowId(c);
        var window = await WindowOf(windowId, c);
        var before = Fixture("rc03-before.md", "# RC03 before\n");
        var during = Fixture("rc03-during.md", "# RC03 during\n");
        var folder = FixtureFolder("rc03-folder");
        try
        {
            await c.Call("test.recent.clear", new { windowId, what = "all" });
            var id = await Open(c, before);
            await c.Call("document.close", new { documentId = id });
            await c.Call("test.folder.open", new { windowId, path = folder });
            await Eventually(async () => (await RecentMenu(c, windowId)).Count >= 2);

            // Settings > General > Data: the switch turned off as a person does.
            await c.Call("test.window.navigate", new { windowId, route = "Settings/General" });
            await Activate(window);
            var toggle = await WaitFor(window, "RecordRecentFiles");
            Invoke(toggle);
            await Task.Delay(500);
            var off = (bool)(await c.Call("test.settings.get", new { windowId, name = "RecordRecentFiles" }))["value"]!;
            Check(!off, "the switch turns recording off");
            await c.Call("test.window.navigate", new { windowId, route = "Main" });
            await Task.Delay(800);
            var menuBefore = await RecentMenu(c, windowId);
            id = await Open(c, during);
            await Task.Delay(1000);
            await c.Call("document.close", new { documentId = id });
            var menu = await RecentMenu(c, windowId);
            notes.Add("recording off, after opening rc03-during.md: " + Show(menu));
            Check(!menu.Any(e => SamePath(e.Path, during)), "with recording off a file opened is not added");
            Check(menu.Count == menuBefore.Count, "with recording off the lists stay as they were");

            // Clear recent: files and folders.
            await c.Call("test.window.navigate", new { windowId, route = "Settings/General" });
            await Activate(window);
            Invoke(await WaitFor(window, "ClearRecentHistory"));
            await Task.Delay(800);
            await c.Call("test.window.navigate", new { windowId, route = "Main" });
            await Task.Delay(800);
            var emptied = await Eventually(async () => (menu = await RecentMenu(c, windowId)).Count == 0, 4000);
            notes.Add("after Clear recent: " + Show(menu));
            Check(emptied, "Clear recent empties the files and the folders");
        }
        finally
        {
            try { await c.Call("test.window.navigate", new { windowId, route = "Main" }); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "RecordRecentFiles", value = true }); } catch { }
        }
    }
}
