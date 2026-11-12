using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// XM01: the editor's context menu in each place it opens (the 2026-11-11 menu report: nothing of it had been seen on
/// Windows), read through UI Automation after a real right-click: the items there, and the ones that must not be.
/// - a paragraph: Copy As (plain text, Markdown, HTML), Paste and Paste as Plain Text at the top; no object items;
/// - a link: Open Link, Copy Link Address (the address on the clipboard), Remove Link;
/// - a code block: Copy Code; a display formula: Copy Formula; a table cell: the Table submenu with Copy table;
/// - source mode: no Copy As, no Paste as Plain Text, no object items;
/// - reading mode: no Cut, Paste or Remove Link; Open Link and Copy Link Address still there;
/// - File > Open Recent: an entry's right-click opens its menu, whose Remove from list takes it out.
/// </summary>
internal static partial class Program
{
    /// <summary>
    /// XM02: File > Open Recent, an entry's menu, Remove from list - with a real right-click and click, twice in a row:
    /// each time the entry right-clicked is the one gone, the others stay, and the menus open again for the next (the
    /// user, 2026-11-12: it took out another entry than the one clicked, and a second could not be taken out).
    /// </summary>
    private static async Task XM02(List<string> notes)
    {
        var folder = FixtureFolder("xm02");
        var paths = Enumerable.Range(1, 4).Select(i => Path.Combine(folder, $"xm02-{i}.md")).ToList();
        foreach (var p in paths) File.WriteAllText(p, "# " + Path.GetFileNameWithoutExtension(p) + "\n");
        using var c = await Session("e2e XM02");
        var keep = await Open(c, paths[0]);
        var windowId = await WindowIdOf(c, keep);
        var window = await WindowOf(windowId, c);
        try
        {
            await c.Call("test.recent.clear", new { windowId });
            // Each opened and closed: the recent list, the last first.
            foreach (var p in paths.Skip(1))
            {
                var id = await Open(c, p);
                await Task.Delay(300);
                await c.Call("document.close", new { documentId = id });
                await Task.Delay(300);
            }
            List<string> Files(List<RecentEntry> entries) => entries.Where(e => e.Kind == "file").Select(e => e.Path).ToList();
            var before = Files(await RecentMenu(c, windowId));
            notes.Add("recent: " + string.Join(", ", before.Select(Path.GetFileName)));
            Check(before.Count >= 3, $"at least three recent files ({before.Count})");
            foreach (var (round, index) in new[] { (1, 1), (2, 0) })
            {
                var entry = await MenuEntry(window, "MenuBarFileItem", "OpenRecentSubMenu", $"RecentFile-{index}");
                var target = entry.Current.HelpText;
                notes.Add($"round {round}: right-click on {entry.Current.Name} ({Path.GetFileName(target)})");
                await Click(window, entry, Mouse.Right);
                await Task.Delay(600);
                var remove = await MenuEntry(window, "RecentRemoveItem");
                await Click(window, remove, Mouse.Left);
                await Task.Delay(1000);
                var after = Files(await RecentMenu(c, windowId));
                notes.Add($"round {round}: recent now {string.Join(", ", after.Select(Path.GetFileName))}");
                Check(!after.Any(p => SamePath(p, target)), $"round {round}: the entry right-clicked ({Path.GetFileName(target)}) is gone");
                var others = before.Where(p => !SamePath(p, target)).ToList();
                Check(others.All(p => after.Any(a => SamePath(a, p))), $"round {round}: the others stay ({string.Join(", ", others.Select(Path.GetFileName))} against {string.Join(", ", after.Select(Path.GetFileName))})");
                before = after;
                await CloseMenus();
            }
        }
        finally
        {
            await CloseMenus();
            try { await c.Call("document.close", new { documentId = keep }); } catch { }
        }
    }

    private static async Task XM01(List<string> notes)
    {
        var folder = FixtureFolder("xm01");
        var path = Path.Combine(folder, "xm01.md");
        File.WriteAllText(path, "# Context menus\n\nA plain paragraph to right-click.\n\nA paragraph with a [link to the site](https://example.com/xm01) in it.\n\n```js\nconst answer = 42;\n```\n\n$$\nE = mc^2\n$$\n\n| a | b |\n| --- | --- |\n| 1 | 2 |\n\nAfter.\n");
        using var c = await Session("e2e XM01");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        await Task.Delay(1500);
        async Task<Dictionary<string, System.Windows.Automation.AutomationElement>> MenuAt(string where, string selector, string? awaiting = null)
        {
            // The menu before gone first: closed, its items could still be read for a moment, and were taken for this
            // menu's (a code block's menu "had" the link's items, XM01 on w10; the host's log had it right).
            var gone = await Eventually(() => Task.FromResult(FindAnywhereInHost(window, "SelectAllItem") == null), 3000, 100);
            if (!gone) notes.Add($"{where}: the menu before was still readable after 3 s");
            var menu = await ContextMenuAt(c, windowId, window, selector, awaiting);
            notes.Add($"{where}: {string.Join(", ", menu.Keys.OrderBy(k => k))}");
            return menu;
        }
        void Close() { Send(Key(0x1B, false), Key(0x1B, true)); }
        void Has(Dictionary<string, System.Windows.Automation.AutomationElement> menu, string where, params string[] ids)
        {
            foreach (var item in ids) Check(menu.ContainsKey(item), $"{where}: the menu has {item} ({string.Join(", ", menu.Keys)})");
        }
        void HasNot(Dictionary<string, System.Windows.Automation.AutomationElement> menu, string where, params string[] ids)
        {
            foreach (var item in ids) Check(!menu.ContainsKey(item), $"{where}: the menu has no {item}");
        }
        string[] objects = { "CopyTableItem", "TableSubItem", "OpenLinkItem", "CopyLinkAddressItem", "RemoveLinkItem", "CopyCodeItem", "CopyFormulaItem", "CopyDiagramItem" };
        try
        {
            // A paragraph.
            var paragraph = await MenuAt("paragraph", "#ag-editor-id > p:nth-of-type(1)");
            Close(); await Task.Delay(300);
            Has(paragraph, "a paragraph", "CopyItem", "CopyAsSubItem", "PasteItem", "PasteAsPlainTextItem", "CutItem", "UndoItem", "RedoItem");
            HasNot(paragraph, "a paragraph", objects);

            // A link: its items, and its address copied.
            var link = await MenuAt("link", "#ag-editor-id a[href]", "CopyLinkAddressItem");
            Has(link, "a link", "OpenLinkItem", "CopyLinkAddressItem", "RemoveLinkItem");
            HasNot(link, "a link", "TableSubItem", "CopyCodeItem");
            if (link.TryGetValue("CopyLinkAddressItem", out var copyAddress))
            {
                EmptyTheClipboard();
                ((System.Windows.Automation.InvokePattern)copyAddress.GetCurrentPattern(System.Windows.Automation.InvokePattern.Pattern)).Invoke();
                await Task.Delay(500);
                var text = CaptureClipboard(n => n == "13").Select(f => Encoding.Unicode.GetString(f.bytes).TrimEnd('\0')).FirstOrDefault();
                notes.Add($"copied link address: {text}");
                Check(text == "https://example.com/xm01", $"Copy Link Address put the address on the clipboard ({text})");
            }
            else Close();
            await Task.Delay(300);

            var code = await MenuAt("code block", "#ag-editor-id pre[data-role=fencecode] code", "CopyCodeItem");
            Close(); await Task.Delay(300);
            Has(code, "a code block", "CopyCodeItem");
            HasNot(code, "a code block", "OpenLinkItem", "TableSubItem");

            var formula = await MenuAt("formula", "#ag-editor-id figure[data-role=MULTIPLEMATH]", "CopyFormulaItem");
            Close(); await Task.Delay(300);
            Has(formula, "a display formula", "CopyFormulaItem");

            // Copy table at the top (one click, as it was); the rows and columns in the Table submenu.
            var table = await MenuAt("table cell", "#ag-editor-id figure[data-role=TABLE] tbody td", "CopyTableItem");
            Has(table, "a table cell", "CopyTableItem", "TableSubItem");
            if (table.ContainsKey("TableSubItem"))
            {
                var rowAbove = await MenuEntry(window, "TableSubItem", "TableRowAboveItem");
                Check(rowAbove != null, "the Table submenu has Insert Row Above");
            }
            Close(); Close(); await Task.Delay(300);

            // Source mode: what is the visual editor's goes.
            await c.Call("test.settings.set", new { windowId, name = "SourceCode", value = true });
            await Task.Delay(1500);
            var source = await MenuAt("source mode", ".CodeMirror-line");
            Close(); await Task.Delay(300);
            HasNot(source, "source mode", "CopyAsSubItem", "PasteAsPlainTextItem", "MenuFormatItem");
            HasNot(source, "source mode", objects);
            Has(source, "source mode", "CopyItem", "PasteItem");
            await c.Call("test.settings.set", new { windowId, name = "SourceCode", value = false });
            await Task.Delay(1500);

            // Reading mode: nothing that changes the document.
            await c.Call("test.settings.set", new { windowId, name = "ReadOnly", value = true });
            await Task.Delay(1200);
            var reading = await MenuAt("reading, link", "#ag-editor-id a[href]", "CopyLinkAddressItem");
            Close(); await Task.Delay(300);
            HasNot(reading, "reading mode", "CutItem", "PasteItem", "RemoveLinkItem", "UndoItem");
            Has(reading, "reading mode", "CopyItem", "OpenLinkItem", "CopyLinkAddressItem");
            await c.Call("test.settings.set", new { windowId, name = "ReadOnly", value = false });
            await Task.Delay(800);

            // File > Open Recent: an entry's right-click opens its menu (it took the entry out at once).
            var entry = await MenuEntry(window, "MenuBarFileItem", "OpenRecentSubMenu", "RecentFile-0");
            notes.Add($"recent entry: {entry.Current.HelpText}");
            await Click(window, entry, Mouse.Right);
            await Task.Delay(600);
            System.Windows.Automation.AutomationElement? remove = null;
            try { remove = await MenuEntry(window, "RecentRemoveItem"); } catch (CaseFailed) { }
            Check(remove != null, "a recent entry's right-click opens its menu with Remove from list");
            // A picture of it over the menus: it came up see-through, with no background (the user, 2026-11-12).
            notes.Add("the entry's menu: " + Screenshot("xm01-recent-entry-menu"));
            // The menus stay open under the entry's menu, and go once one of its items is chosen (they went as it opened).
            var still = FindAnywhereInHost(window, "RecentFile-0");
            Check(still != null && !still.Current.IsOffscreen, "the Open Recent menu stays open under the entry's menu");
            if (remove != null)
            {
                await Click(window, remove, Mouse.Left);
                await Task.Delay(800);
                var left = FindAnywhereInHost(window, "OpenRecentSubMenu");
                Check(left == null || left.Current.IsOffscreen, "a choice in the entry's menu closes the menus");
            }
            await CloseMenus();
        }
        finally
        {
            await CloseMenus();
            try { await c.Call("test.settings.set", new { windowId, name = "SourceCode", value = false }); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "ReadOnly", value = false }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
