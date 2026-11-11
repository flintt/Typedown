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
        string[] objects = { "TableSubItem", "OpenLinkItem", "CopyLinkAddressItem", "RemoveLinkItem", "CopyCodeItem", "CopyFormulaItem", "CopyDiagramItem" };
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

            var table = await MenuAt("table cell", "#ag-editor-id figure[data-role=TABLE] tbody td", "TableSubItem");
            Has(table, "a table cell", "TableSubItem");
            if (table.ContainsKey("TableSubItem"))
            {
                var copyTable = await MenuEntry(window, "TableSubItem", "CopyTableItem");
                var rowAbove = await MenuEntry(window, "TableRowAboveItem");
                Check(copyTable != null && rowAbove != null, "the Table submenu has Copy table and Insert Row Above");
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
