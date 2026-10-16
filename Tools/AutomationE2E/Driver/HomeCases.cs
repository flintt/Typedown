using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    /// <summary>
    /// A row of the folder pane's file tree by its name - in the tree itself (automation id TreeView), not the
    /// favourites group above it, which can list the same name (FV01 right-clicked the favourite and got its menu).
    /// </summary>
    private static AutomationElement? TreeRow(IntPtr window, string name)
    {
        var tree = FindIn(window, "TreeView");
        if (tree == null)
        {
            // No automation id on the tree: any row of that name not inside the favourites list.
            var favorites = FindIn(window, "FavoritesList");
            foreach (AutomationElement candidate in Deep.All(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.NameProperty, name)))
            {
                var inside = false;
                for (var node = TreeWalker.ControlViewWalker.GetParent(candidate); node != null && !inside; node = TreeWalker.ControlViewWalker.GetParent(node))
                    inside = favorites != null && Automation.Compare(node, favorites);
                if (!inside) return candidate;
            }
            return null;
        }
        return Deep.First(tree, new AndCondition(new PropertyCondition(AutomationElement.NameProperty, name), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem)))
            ?? Deep.First(tree, new PropertyCondition(AutomationElement.NameProperty, name));
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

    // ---- the folder pane's navigation ----

    private static async Task FN01(List<string> notes)
    {
        using var c = await Session("e2e FN01");
        // fixtures/fn01/one/two, with a Markdown file in each and the tree rooted at "two".
        var top = FixtureFolder("fn01");
        var one = Path.Combine(top, "one");
        var two = Path.Combine(one, "two");
        Directory.CreateDirectory(two);
        File.WriteAllText(Path.Combine(one, "one.md"), "# one\n");
        File.WriteAllText(Path.Combine(two, "two.md"), "# two\n");
        Directory.CreateDirectory(Path.Combine(two, "three"));
        File.WriteAllText(Path.Combine(two, "three", "three.md"), "# three\n");
        var a = await Open(c, Fixture("fn01-a.md", "# FN01 a\n"));
        var b = await Open(c, Fixture("fn01-b.md", "# FN01 b\n"));
        var windowId = await WindowIdOf(c, b);
        var window = await WindowOf(windowId, c);
        async Task<List<string>> Documents() =>
            ((JArray)(await c.Call("document.list", new { windowId }))["documents"]!).Select(d => (string)d["documentId"]!).ToList();
        async Task<string> Active() =>
            (string)((JArray)(await c.Call("window.list"))["windows"]!).First(w => (string)w["windowId"]! == windowId)["activeDocumentId"]!;
        async Task<bool> RootIs(string folder) => await Eventually(async () => SamePath((string?)(await FolderState(c, windowId))["root"], folder), 5000);
        var docsBefore = await Documents();
        var activeBefore = await Active();
        try
        {
            Check((bool)(await c.Call("test.folder.open", new { windowId, path = two }))["opened"]!, "the folder opens");
            Check(await RootIs(two), "the tree is rooted at two");

            // The up arrow.
            await Click(window, await WaitFor(window, "FolderUpButton"));
            Check(await RootIs(one), "the up arrow goes to one");

            // The root's menu: the folders above it, the drive last.
            var menu = ((JArray)(await c.Call("test.folder.menu", new { windowId }))["entries"]!).ToList();
            notes.Add("root menu: " + string.Join(", ", menu.Select(e => $"{e["id"]}={e["text"]}")));
            Check(menu.Any(e => (string)e["id"]! == "FolderNav-Ancestor-0" && SamePath((string?)e["path"], top)), "the menu's first folder above is fn01");
            Check(menu.Any(e => (string)e["id"]! == "FolderNav-OpenFolder"), "the menu offers Open folder");
            await Click(window, await WaitFor(window, "FolderRootButton"));
            await Task.Delay(600);
            var ancestor = await MenuEntry(window, "FolderNav-Ancestor-0");
            await ClickInPopup(window, ancestor);
            Check(await RootIs(top), "choosing the folder above in the menu makes it the root");

            // Back to two, then "Set as root" on its subfolder "three" in the tree.
            await c.Call("test.folder.open", new { windowId, path = two });
            Check(await RootIs(two), "back at two");
            AutomationElement? row = null;
            await Eventually(() => Task.FromResult((row = TreeRow(window, "three")) != null), 5000);
            Check(row != null, "the folder three is in the tree");
            await Click(window, row!, Mouse.Right);
            await Task.Delay(600);
            await ClickInPopup(window, await MenuEntry(window, "FolderSetAsRoot"));
            Check(await RootIs(Path.Combine(two, "three")), "Set as root on three makes it the root");

            var docsAfter = await Documents();
            var activeAfter = await Active();
            notes.Add($"documents before {docsBefore.Count}, after {docsAfter.Count}");
            Check(docsBefore.SequenceEqual(docsAfter), "the open documents are the same ones");
            Check(activeAfter == activeBefore, "the active document did not change");
        }
        finally
        {
            await CloseMenus();
            foreach (var id in new[] { a, b }) try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }

    /// <summary>
    /// FN02: with a folder opened (so the tree no longer follows the document), the button beside the root's name that
    /// shows for a document outside the tree moves the tree to that document's folder. It did nothing: the opened
    /// folder won over the button.
    /// </summary>
    private static async Task FN02(List<string> notes)
    {
        using var c = await Session("e2e FN02");
        var folder = FixtureFolder("fn02");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "inside.md"), "# inside\n");
        var path = Fixture("fn02-outside.md", "# FN02 outside\n");
        var a = await Open(c, path);
        var windowId = await WindowIdOf(c, a);
        var window = await WindowOf(windowId, c);
        async Task<bool> RootIs(string f) => await Eventually(async () => SamePath((string?)(await FolderState(c, windowId))["root"], f), 5000);
        try
        {
            Check((bool)(await c.Call("test.folder.open", new { windowId, path = folder }))["opened"]!, "the folder opens");
            Check(await RootIs(folder), "the tree is rooted at the opened folder");
            await Click(window, await WaitFor(window, "FolderRevealButton"));
            var moved = await RootIs(Path.GetDirectoryName(path)!);
            notes.Add($"root after the click: {(await FolderState(c, windowId))["root"]}");
            Check(moved, "the click moves the tree to the document's folder");
            await WaitGone(window, "FolderRevealButton");
        }
        finally
        {
            try { await c.Call("document.close", new { documentId = a }); } catch { }
        }
    }

    /// <summary>
    /// FW01: an open document renamed outside the app is followed by its window (the tab's path becomes the new name), also
    /// after the app itself renamed it once (the folder pane's Rename): the watcher must move to the new name each time.
    /// </summary>
    private static async Task FW01(List<string> notes)
    {
        using var c = await Session("e2e FW01");
        var folder = FixtureFolder("fw01");
        Directory.CreateDirectory(folder);
        foreach (var f in Directory.GetFiles(folder)) File.Delete(f);
        var first = Path.Combine(folder, "fw01.md");
        File.WriteAllText(first, "# FW01\n");
        var id = await Open(c, first);
        var windowId = await WindowIdOf(c, id);
        async Task<string?> PathAfter(string expected)
        {
            string? path = null;
            for (var i = 0; i < 25 && !string.Equals(path, expected, StringComparison.OrdinalIgnoreCase); i++) { await Task.Delay(200); path = (string?)(await Get(c, id))["path"]; }
            return path;
        }
        try
        {
            await Task.Delay(1500);
            var outside1 = Path.Combine(folder, "fw01-outside.md");
            File.Move(first, outside1);
            var path = await PathAfter(outside1);
            notes.Add("after the first outside rename: " + Path.GetFileName(path ?? "(none)"));
            Check(SamePath(path, outside1), "renamed outside, the window follows");

            var inApp = Path.Combine(folder, "fw01-in-app.md");
            Check((bool?)(await c.Call("test.file.rename", new { windowId, path = inApp }))["renamed"] == true, "renamed in the app");
            path = await PathAfter(inApp);
            Check(SamePath(path, inApp), "renamed in the app, the window has the new name");
            await Task.Delay(1500);
            var outside2 = Path.Combine(folder, "fw01-outside-again.md");
            File.Move(inApp, outside2);
            path = await PathAfter(outside2);
            notes.Add("after the in-app rename and another outside: " + Path.GetFileName(path ?? "(none)"));
            Check(SamePath(path, outside2), "renamed outside after an in-app rename, the window follows");
        }
        finally
        {
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }

    // ---- favourites ----

    /// <summary>
    /// The test host ended and started again on the same data folder (as R04 does): what a restart keeps is what is on
    /// disk. Waits for its window. A dialog the restart brings (recovered backups) is closed before the next case.
    /// </summary>
    private static async Task RestartHost(List<string> notes)
    {
        // What the settings and the favourites hold is written first: a kill gives them no chance to.
        try
        {
            using var pre = await Session("e2e flush");
            foreach (var w in (JArray)(await pre.Call("window.list"))["windows"]!)
                await pre.Call("test.settings.flush", new { windowId = (string)w["windowId"]! });
        }
        catch (Exception e) { notes.Add("flush before the restart: " + e.Message.Split('\n')[0]); }
        using (var dying = Process.GetProcessById(hostPid))
        {
            dying.Kill();
            dying.WaitForExit(10000);
        }
        File.Delete(Path.Combine(testRoot, "automation-endpoint.txt"));
        var restarted = Process.Start(new System.Diagnostics.ProcessStartInfo(hostExe, $"--automation-test-root \"{testRoot}\"") { UseShellExecute = true })!;
        hostPid = restarted.Id;
        endpoint = await ReadEndpointFile(Path.Combine(testRoot, "automation-endpoint.txt")) ?? throw new CaseFailed("the restarted host never published its endpoint");
        using var probe = await Connect();
        await Initialize(probe, "e2e restart");
        await WaitForWindow(probe);
        await Task.Delay(3000);
        try { await probe.Call("test.dialogs.dismiss", new { }); } catch { }
        notes.Add($"restarted, pid {hostPid}");
    }

    private static async Task<(List<(string Path, bool Folder)> Stored, List<(string Path, bool Missing)>? Pane)> FavoritesState(Client c, string windowId)
    {
        var state = await c.Call("test.favorites.state", new { windowId });
        var stored = ((JArray)state["stored"]!).Select(e => ((string)e["path"]!, (bool)e["folder"]!)).ToList();
        var pane = state["pane"] is JArray p ? p.Select(e => ((string)e["path"]!, (bool)e["missing"]!)).ToList() : null;
        return (stored, pane);
    }

    private static async Task FV01(List<string> notes)
    {
        var folder = FixtureFolder("fv01");
        var sub = Path.Combine(folder, "chapter");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "inside.md"), "# inside\n");
        var file = Path.Combine(folder, "fv01.md");
        var renamed = Path.Combine(folder, "fv01-renamed.md");
        if (File.Exists(renamed)) File.Delete(renamed);
        using (var c = await Session("e2e FV01"))
        {
            var windowId = await FirstWindowId(c);
            await c.Call("test.favorites.clear", new { });
            var id = await Open(c, file);
            windowId = await WindowIdOf(c, id);
            var window = await WindowOf(windowId, c);
            await c.Call("test.settings.set", new { windowId, name = "AlwaysShowTabBar", value = true });
            await c.Call("test.folder.open", new { windowId, path = folder });
            await Task.Delay(1000);
            try
            {
                // The file from its tab's right-click menu.
                AutomationElement? tab = null;
                await Eventually(() => Task.FromResult((tab = Deep.All(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem))
                    .Cast<AutomationElement>().FirstOrDefault(t => t.Current.Name.Contains("fv01"))) != null), 5000);
                Check(tab != null, "the document's tab is in the strip");
                await Click(window, tab!, Mouse.Right);
                await Task.Delay(600);
                await ClickInPopup(window, await MenuEntry(window, "TabFavoriteItem"));
                await Task.Delay(500);

                // The folder from the tree's right-click menu.
                AutomationElement? row = null;
                await Eventually(() => Task.FromResult((row = TreeRow(window, "chapter")) != null), 5000);
                Check(row != null, "the folder chapter is in the tree");
                await Click(window, row!, Mouse.Right);
                await Task.Delay(600);
                await ClickInPopup(window, await MenuEntry(window, "TreeFavoriteItem"));
                await Task.Delay(800);

                var (stored, pane) = await FavoritesState(c, windowId);
                notes.Add("favourites: " + string.Join(", ", stored.Select(x => $"{Path.GetFileName(x.Path)}{(x.Folder ? "/" : "")}")));
                Check(stored.Any(x => SamePath(x.Path, file) && !x.Folder), "the file is a favourite (from its tab's menu)");
                Check(stored.Any(x => SamePath(x.Path, sub) && x.Folder), "the folder is a favourite (from the tree's menu)");
                Check(pane != null && pane.Count == 2, "the folder pane's favourites group shows both");
                Check(File.Exists(Path.Combine(testRoot, "data", "favorites.json")), "favorites.json is beside the settings");
            }
            finally
            {
                await CloseMenus();
                try { await c.Call("document.close", new { documentId = id }); } catch { }
                try { await c.Call("test.settings.set", new { windowId, name = "AlwaysShowTabBar", value = false }); } catch { }
            }
        }

        await RestartHost(notes);

        using (var c = await Session("e2e FV01 after"))
        {
            var windowId = await FirstWindowId(c);
            var window = await WindowOf(windowId, c);
            await c.Call("test.folder.open", new { windowId, path = folder });
            List<(string Path, bool Missing)>? pane = null;
            await Eventually(async () => (pane = (await FavoritesState(c, windowId)).Pane)?.Count == 2, 8000);
            notes.Add("after the restart, the pane: " + string.Join(", ", pane?.Select(x => Path.GetFileName(x.Path)) ?? Array.Empty<string>()));
            Check(pane?.Count == 2, "after a restart the favourites group still has the file and the folder");
            await HomeAfterRestartCheck(c, windowId, notes);

            // Renamed in the tree: the favourite follows.
            AutomationElement? row = null;
            await Eventually(() => Task.FromResult((row = TreeRow(window, "fv01.md")) != null), 5000);
            Check(row != null, "fv01.md is in the tree");
            await Click(window, row!, Mouse.Right);
            await Task.Delay(600);
            await ClickInPopup(window, await MenuEntry(window, "TreeRenameItem"));
            await Task.Delay(800);
            Chord("Ctrl+A");
            TypeText("fv01-renamed.md");
            Chord("Enter");
            await Task.Delay(1500);
            var (stored, _) = await FavoritesState(c, windowId);
            notes.Add("after the rename: " + string.Join(", ", stored.Select(x => Path.GetFileName(x.Path))));
            Check(File.Exists(renamed), "the file was renamed on disk");
            Check(stored.Any(x => SamePath(x.Path, renamed)) && !stored.Any(x => SamePath(x.Path, file)), "the favourite follows the rename");
        }
    }

    /// <summary>FV01 after the restart: the home page (shown at start-up by default) lists the favourites too.</summary>
    private static async Task HomeAfterRestartCheck(Client c, string windowId, List<string> notes)
    {
        JToken? state = null;
        var listed = await Eventually(async () => ((JArray?)(state = await HomeState(c, windowId))["lists"]?["favorites"])?.Count == 2, 6000);
        notes.Add("home page favourites: " + state?["lists"]?["favorites"]?.ToString(Formatting.None));
        Check(listed, "after a restart the home page lists the two favourites");
    }

    // ---- the home page ----

    private static Task<JToken> HomeState(Client c, string windowId) => c.Call("test.home.state", new { windowId });

    private static async Task<bool> HomeShown(Client c, string windowId, bool shown, int timeoutMs = 4000) =>
        await Eventually(async () =>
        {
            var state = await HomeState(c, windowId);
            return (bool)state["visible"]! == shown && (bool)state["shown"]! == shown;
        }, timeoutMs);

    private static async Task<List<string>> DocumentsOf(Client c, string windowId) =>
        ((JArray)(await c.Call("document.list", new { windowId }))["documents"]!).Select(d => (string)d["documentId"]!).ToList();

    private static async Task<string> ActiveOf(Client c, string windowId) =>
        (string)((JArray)(await c.Call("window.list"))["windows"]!).First(w => (string)w["windowId"]! == windowId)["activeDocumentId"]!;

    /// <summary>An untitled document a case typed into, put back to empty (its undo) and closed, so nothing asks to be saved.</summary>
    private static async Task UndoAndClose(Client c, string documentId, List<string> notes)
    {
        try
        {
            for (var i = 0; i < 5 && ((string?)(await Get(c, documentId))["text"] ?? "").Trim().Length > 0; i++)
                await c.Call("document.undo", new { documentId, baseRevision = await Revision(c, documentId), reveal = "document" });
            await c.Call("document.close", new { documentId });
        }
        catch (Exception e) { notes.Add("cleanup: " + e.Message.Split('\n')[0]); }
    }

    private static async Task HP01(List<string> notes)
    {
        var file = Fixture("hp01.md", "# HP01\n\nOpened from the home page.\n");
        using (var c = await Session("e2e HP01 before"))
        {
            var windowId = await FirstWindowId(c);
            // The settings as they come (the home page on, nothing opened at start-up), and hp01.md a recent file.
            await c.Call("test.settings.set", new { windowId, name = "ShowHomePage", value = true });
            await c.Call("test.settings.set", new { windowId, name = "FileStartupAction", value = 0 });
            var id = await Open(c, file);
            await c.Call("document.close", new { documentId = id });
        }
        await RestartHost(notes);
        using (var c = await Session("e2e HP01"))
        {
            var windowId = await FirstWindowId(c);
            var window = await WindowOf(windowId, c);
            Check(await HomeShown(c, windowId, true, 8000), "a start with the settings as they come shows the home page");
            var before = await DocumentsOf(c, windowId);
            AutomationElement? entry = null;
            await Eventually(() => Task.FromResult((entry = Deep.First(AutomationElement.FromHandle(window), new AndCondition(
                new PropertyCondition(AutomationElement.NameProperty, "hp01.md"), new PropertyCondition(AutomationElement.HelpTextProperty, file)))) != null), 6000);
            Check(entry != null, "the home page lists hp01.md among the recent files");
            await Click(window, entry!);
            Check(await HomeShown(c, windowId, false), "opening a recent file from the home page hides it");
            var after = await DocumentsOf(c, windowId);
            var active = await ActiveOf(c, windowId);
            var path = (string?)(await c.Call("document.get", new { documentId = active, consistency = "snapshot" }))["path"];
            notes.Add($"documents {before.Count} -> {after.Count}, active {path}");
            Check(after.Count == 1 && before.Count == 1, "the file opened in the home page's own tab, no tab added");
            Check(SamePath(path, file), "the tab shows hp01.md");
            await c.Call("document.close", new { documentId = active });
        }
    }

    private static async Task HP02(List<string> notes)
    {
        using var c = await Session("e2e HP02");
        var first = await Open(c, Fixture("hp02.md", "# HP02\n"));
        var windowId = await WindowIdOf(c, first);
        var window = await WindowOf(windowId, c);
        var created = new List<string>();
        try
        {
            await c.Call("test.settings.set", new { windowId, name = "ShowHomeOnNewTab", value = false });
            await TypeInto(c, first, "");
            Chord("Ctrl+N");
            await Task.Delay(1500);
            var ctrlN = await ActiveOf(c, windowId);
            created.Add(ctrlN);
            Check(ctrlN != first, "Ctrl+N made a new tab");
            Check(await HomeShown(c, windowId, false, 1500), "Ctrl+N shows no home page");
            TypeChar('N');
            await WaitForPage(c, ctrlN, t => t.Contains('N'), "a letter after Ctrl+N");

            await Click(window, await WaitFor(window, "AddButton"));
            await Task.Delay(1500);
            var plus = await ActiveOf(c, windowId);
            created.Add(plus);
            Check(await HomeShown(c, windowId, false, 1500), "the + button shows no home page");
            TypeChar('P');
            await WaitForPage(c, plus, t => t.Contains('P'), "a letter after the + button");

            // "New tabs show the home page" on.
            await c.Call("test.settings.set", new { windowId, name = "ShowHomeOnNewTab", value = true });
            await Activate(window);
            Chord("Ctrl+N");
            await Task.Delay(1500);
            var shown = await ActiveOf(c, windowId);
            created.Add(shown);
            Check(await HomeShown(c, windowId, true), "with \"New tabs show the home page\" on, Ctrl+N shows it");
        }
        finally
        {
            try { await c.Call("test.settings.set", new { windowId, name = "ShowHomeOnNewTab", value = false }); } catch { }
            foreach (var doc in created.Distinct()) await UndoAndClose(c, doc, notes);
            try { await c.Call("document.close", new { documentId = first }); } catch { }
        }
    }

    private static async Task HP03(List<string> notes)
    {
        string windowId;
        using (var c = await Session("e2e HP03"))
        {
            windowId = (string)(await c.Call("test.window.open"))["windowId"]!;
            var window = await WindowOf(windowId, c);
            await Task.Delay(4000);
            Check(await HomeShown(c, windowId, true, 6000), "a new window with nothing to open shows the home page");
            var id = (string)(await c.Call("document.open", new { path = Fixture("hp03.md", "# HP03\n"), windowId, reveal = "document" }))["documentId"]!;
            Check(await HomeShown(c, windowId, false), "a file opened in it hides the home page");
            await Activate(window);
            Chord("Ctrl+W");
            await Task.Delay(1500);
            Check(IsWindow(window), "closing the last tab leaves the window open");
            Check(await HomeShown(c, windowId, true), "closing the last tab shows the home page");
            Check((await DocumentsOf(c, windowId)).Count == 1, "one tab, the blank one");
            // Closed there again: the window goes.
            Chord("Ctrl+W");
            await Task.Delay(1500);
            Check(!IsWindow(window), "the last tab closed on the home page closes the window");
        }

        // The last session's files all gone: the home page. The switch off: a blank document.
        var gone = Path.Combine(fixtures, "hp03-gone.md");
        if (File.Exists(gone)) File.Delete(gone);
        var session = Path.Combine(testRoot, "data", "session.json");
        using (var c = await Session("e2e HP03 session"))
        {
            var w = await FirstWindowId(c);
            await c.Call("test.settings.set", new { windowId = w, name = "FileStartupAction", value = 2 });
        }
        try
        {
            File.WriteAllText(session, JsonConvert.SerializeObject(new { Files = new[] { gone }, ActiveIndex = 0, Time = DateTime.UtcNow }));
            await RestartHost(notes);
            using (var c = await Session("e2e HP03 restored"))
            {
                var w = await FirstWindowId(c);
                Check(await HomeShown(c, w, true, 8000), "a session whose files are all gone shows the home page");
                await c.Call("test.settings.set", new { windowId = w, name = "ShowHomePage", value = false });
            }
            File.WriteAllText(session, JsonConvert.SerializeObject(new { Files = new[] { gone }, ActiveIndex = 0, Time = DateTime.UtcNow }));
            await RestartHost(notes);
            using (var c = await Session("e2e HP03 off"))
            {
                var w = await FirstWindowId(c);
                await Task.Delay(3000);
                var state = await HomeState(c, w);
                notes.Add("switch off: " + state.ToString(Formatting.None));
                Check(!(bool)state["visible"]! && (bool)state["blank"]!, "with the switch off the start shows a blank document");
            }
        }
        finally
        {
            using var c = await Session("e2e HP03 restore");
            var w = await FirstWindowId(c);
            try { await c.Call("test.settings.set", new { windowId = w, name = "ShowHomePage", value = true }); } catch { }
            try { await c.Call("test.settings.set", new { windowId = w, name = "FileStartupAction", value = 0 }); } catch { }
        }
    }

    private static async Task HP04(List<string> notes)
    {
        using var c = await Session("e2e HP04");
        var a = await Open(c, Fixture("hp04-a.md", "# HP04 a\n"));
        var windowId = await WindowIdOf(c, a);
        var b = (string)(await c.Call("document.open", new { path = Fixture("hp04-b.md", "# HP04 b\n"), windowId, reveal = "document" }))["documentId"]!;
        var window = await WindowOf(windowId, c);
        string? blank = null;
        try
        {
            var before = await DocumentsOf(c, windowId);
            // File > Home.
            await ClickInPopup(window, await MenuEntry(window, "MenuBarFileItem", "HomeMenuItem"));
            Check(await HomeShown(c, windowId, true), "File > Home shows the home page");
            blank = await ActiveOf(c, windowId);
            var withHome = await DocumentsOf(c, windowId);
            Check(withHome.Count == before.Count + 1 && before.All(withHome.Contains), "in a new tab, the open documents left as they were");

            // The ⌂ button, from another tab: the blank tab is used again.
            await c.Call("document.focus", new { documentId = a });
            Check(await HomeShown(c, windowId, false), "another tab: no home page");
            await Click(window, await WaitFor(window, "HomeButton"));
            Check(await HomeShown(c, windowId, true), "the ⌂ button shows the home page");
            Check(await ActiveOf(c, windowId) == blank && (await DocumentsOf(c, windowId)).Count == withHome.Count, "in the blank tab there already");

            // The shortcut.
            await c.Call("document.focus", new { documentId = b });
            await Task.Delay(500);
            await Activate(window);
            Chord("Ctrl+Shift+H");
            Check(await HomeShown(c, windowId, true), "Ctrl+Shift+H shows the home page");
            Check(await ActiveOf(c, windowId) == blank, "the shortcut goes to the same blank tab");

            // An automation write into the home page's document.
            const string text = "# Written\n\nby a program\n";
            await c.Call("document.replace", new { documentId = blank, baseRevision = await Revision(c, blank), text, normalizationPolicy = "allowUnknown" });
            Check(await HomeShown(c, windowId, false), "a write into its document hides the home page");
            var written = (string?)(await Get(c, blank))["text"];
            Check(written == text, $"the document holds what was written ({JsonConvert.SerializeObject(written)})");
        }
        finally
        {
            if (blank != null) await UndoAndClose(c, blank, notes);
            foreach (var id in new[] { a, b }) try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }

    private static async Task HP05(List<string> notes)
    {
        using var c = await Session("e2e HP05");
        var a = await Open(c, Fixture("hp05.md", "# HP05\n"));
        var windowId = await WindowIdOf(c, a);
        var window = await WindowOf(windowId, c);
        var made = new List<string>();
        try
        {
            // Shown by the shortcut with the caret in a document: the next letter goes into the blank document and the home page goes.
            await TypeInto(c, a, "");
            Chord("Ctrl+Shift+H");
            Check(await HomeShown(c, windowId, true), "the shortcut shows the home page");
            var blank = await ActiveOf(c, windowId);
            made.Add(blank);
            // The new blank document given time to reach the page first, as K05 waits after Ctrl+N: keys typed while a
            // new tab's document is still loading go to the page's previous document and are lost (a tab-loading race of
            // its own, not the home page's).
            await Task.Delay(1500);
            TypeChar('Q');
            await WaitForPage(c, blank, t => t.Contains('Q'), "a letter typed on the home page");
            Check(await HomeShown(c, windowId, false), "typing hides the home page");

            // Shown by the ⌂ button (the keyboard on the button), then New document: the keys go to the editor.
            await c.Call("test.settings.set", new { windowId, name = "AlwaysShowTabBar", value = true });
            await Task.Delay(500);
            await Click(window, await WaitFor(window, "HomeButton"));
            Check(await HomeShown(c, windowId, true), "the ⌂ button shows the home page");
            var second = await ActiveOf(c, windowId);
            made.Add(second);
            await Click(window, await WaitFor(window, "HomeNewButton"));
            Check(await HomeShown(c, windowId, false), "New document hides the home page");
            await Task.Delay(500);
            TypeChar('W');
            var text = await WaitForPage(c, second, t => t.Contains('W'), "a letter after New document");
            notes.Add("after New document: " + JsonConvert.SerializeObject(text));
        }
        finally
        {
            try { await c.Call("test.settings.set", new { windowId, name = "AlwaysShowTabBar", value = false }); } catch { }
            foreach (var doc in made.Distinct()) await UndoAndClose(c, doc, notes);
            try { await c.Call("document.close", new { documentId = a }); } catch { }
        }
    }
}
