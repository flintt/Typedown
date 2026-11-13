using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;

/// <summary>
/// XM09 (the 2026-11-11 menu report's item 5; the user, 2026-11-14): the context menus XM01 and TM01 do not reach, by real
/// right-clicks, read the way a screen reader reads them.
/// - a heading, a list item: the editing items, no object's;
/// - a picture (chosen first, as a person does): the Image submenu;
/// - a plain text file: copy and paste, no format row (its view is source);
/// - a folder in the folder tree: Set as root, New file, no Open in New Window;
/// - a folder among the home page's recent folders: Open, Open File Location, Copy as Path, no Open in New Window;
/// - the keyboard's menu key and Shift+F10 with the caret in a paragraph: the editor's menu.
/// </summary>
internal static partial class Program
{
    private static async Task XM09(List<string> notes)
    {
        var folder = FixtureFolder("xm09");
        var chapter = Directory.CreateDirectory(Path.Combine(folder, "chapter")).FullName;
        var picture = Path.Combine(folder, "dot.png");
        using (var bitmap = new System.Drawing.Bitmap(60, 40))
        {
            using (var g = System.Drawing.Graphics.FromImage(bitmap)) g.Clear(System.Drawing.Color.SteelBlue);
            bitmap.Save(picture, System.Drawing.Imaging.ImageFormat.Png);
        }
        var path = Path.Combine(folder, "xm09.md");
        File.WriteAllText(path, "# A heading here\n\nA plain paragraph with words.\n\n- first item in the list\n- second item\n\n![dot](dot.png)\n\nAfter.\n");
        var text = Path.Combine(folder, "xm09.txt");
        File.WriteAllText(text, "Plain text lines\nanother line\n");
        using var c = await Session("e2e XM09");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        string? textId = null, blank = null;
        string[] objects = { "CopyTableItem", "TableSubItem", "OpenLinkItem", "CopyLinkAddressItem", "RemoveLinkItem", "CopyCodeItem", "CopyFormulaItem", "CopyDiagramItem", "MenuImageItem" };
        void Close() => Send(Key(0x1B, false), Key(0x1B, true));
        async Task<Dictionary<string, AutomationElement>> MenuAt(string where, string selector, string? awaiting = null)
        {
            await Eventually(() => Task.FromResult(FindAnywhereInHost(window, "SelectAllItem") == null), 3000, 100);
            var menu = await ContextMenuAt(c, windowId, window, selector, awaiting);
            notes.Add($"{where}: {string.Join(", ", menu.Keys.Where(k => k.Length > 0).OrderBy(k => k))}");
            return menu;
        }
        void Editing(Dictionary<string, AutomationElement> menu, string where, bool format)
        {
            Check(menu.ContainsKey("CopyItem") && menu.ContainsKey("PasteItem") && menu.ContainsKey("SelectAllItem"), $"{where}: copy, paste and select all");
            Check(menu.ContainsKey("MenuFormatItem") == format, $"{where}: {(format ? "the" : "no")} format row");
        }
        try
        {
            await c.Call("window.setView", new { windowId, mode = "visual", bounds = new { x = 40, y = 40, width = 1300, height = 850 } });
            await Task.Delay(1200);

            // A heading, a list item.
            var heading = await MenuAt("heading", "#ag-editor-id h1");
            Close(); await Task.Delay(300);
            Editing(heading, "a heading", true);
            Check(!objects.Any(heading.ContainsKey), "a heading: no object's items");
            var item = await MenuAt("list item", "#ag-editor-id li");
            Close(); await Task.Delay(300);
            Editing(item, "a list item", true);
            Check(!objects.Any(item.ContainsKey), "a list item: no object's items");

            // A picture, chosen first.
            var at = (await c.Call("test.editor.eval", new { windowId, script = "(() => { const i = document.querySelector('#ag-editor-id img'); if (!i) return null; i.scrollIntoView({ block: 'center' }); const r = i.getBoundingClientRect(); return { x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) } })()" }))["result"]!;
            Check(at.Type == Newtonsoft.Json.Linq.JTokenType.Object, "the picture is drawn");
            var screen = await c.Call("test.editor.screenPoint", new { windowId, x = (int)at["x"]!, y = (int)at["y"]! });
            GetWindowRect(window, out var rc);
            // Its middle, clicked and right-clicked as IU05 does it (20 px in is its status icon's place).
            await Click(window, (int)screen["x"]! - rc.Left, (int)screen["y"]! - rc.Top);
            await Task.Delay(400);
            await Click(window, (int)screen["x"]! - rc.Left, (int)screen["y"]! - rc.Top, Mouse.Right);
            AutomationElement? imageItem = null;
            var hasImage = await Eventually(() => Task.FromResult((imageItem = FindAnywhereInHost(window, "MenuImageItem")) != null), 4000, 150);
            notes.Add($"picture: Image submenu {(hasImage ? "there" : "missing")}");
            Close(); await Task.Delay(300);
            Check(hasImage, "a picture: the Image submenu");

            // The keyboard's menu key and Shift+F10, the caret in a paragraph.
            foreach (var (name, press) in new (string, Action)[] { ("the menu key", () => Send(Key(0x5D, false), Key(0x5D, true))), ("Shift+F10", () => Chord("Shift+F10")) })
            {
                await ClickEditorText(c, windowId, window, "A plain paragraph");
                await Task.Delay(300);
                press();
                var opened = await Eventually(() => Task.FromResult(FindAnywhereInHost(window, "SelectAllItem") != null), 3000, 100);
                notes.Add($"{name}: menu {(opened ? "opened" : "not opened")}");
                Check(opened, $"{name} opens the editor's menu");
                Close(); await Task.Delay(400);
            }

            // A plain text file.
            textId = await Open(c, text);
            await Task.Delay(1500);
            var plain = await MenuAt("plain text", ".CodeMirror-line, #ag-editor-id p");
            Close(); await Task.Delay(300);
            Editing(plain, "a plain text file", false);

            // A folder in the folder tree.
            await c.Call("test.folder.open", new { windowId, path = folder });
            await Task.Delay(1000);
            AutomationElement? row = null;
            await Eventually(() => Task.FromResult((row = TreeRow(window, "chapter")) != null), 5000);
            Check(row != null, "the folder is in the tree");
            await Click(window, row!, Mouse.Right);
            await Task.Delay(700);
            var setRoot = FindAnywhereInHost(window, "FolderSetAsRoot");
            var names = MenuNamesBeside(setRoot);
            notes.Add("tree folder: " + string.Join(", ", names));
            Check(setRoot != null && names.Any(n => n.Contains("New", StringComparison.OrdinalIgnoreCase)) && !names.Any(n => n.Contains("New Window", StringComparison.OrdinalIgnoreCase)),
                "a folder in the tree: Set as root and New file, no Open in New Window");
            await CloseMenus();

            // A folder among the home page's recent folders.
            await ClickInPopup(window, await MenuEntry(window, "MenuBarFileItem", "HomeMenuItem"));
            Check(await HomeShown(c, windowId, true), "File > Home shows the home page");
            blank = await ActiveOf(c, windowId);
            await Task.Delay(1000);
            AutomationElement? recent = null;
            await Eventually(() => Task.FromResult((recent = FindIn(window, "HomeRecentFolders")?.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>()
                .FirstOrDefault(x => x.Current.Name.Contains("xm09", StringComparison.OrdinalIgnoreCase) || SamePath(x.Current.HelpText, folder))) != null), 5000);
            Check(recent != null, "the folder is among the home page's recent folders");
            await Click(window, recent!, Mouse.Right);
            await Task.Delay(700);
            var homeNames = ShownMenuNames(window);
            notes.Add("home recent folder: " + string.Join(", ", homeNames));
            Check(homeNames.Contains("Open") && homeNames.Any(n => n.Contains("File Location")) && homeNames.Any(n => n.Contains("Copy as Path")) && !homeNames.Any(n => n.Contains("New Window")),
                "a recent folder on the home page: Open, Open File Location, Copy as Path, no Open in New Window");
            await CloseMenus();
        }
        finally
        {
            await CloseMenus();
            if (blank != null) try { await c.Call("document.close", new { documentId = blank }); } catch { }
            if (textId != null) try { await c.Call("document.close", new { documentId = textId }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }

    // The entries of the menu holding the element, top to bottom, as shown.
    private static List<string> MenuNamesBeside(AutomationElement? anyEntry)
    {
        if (anyEntry == null) return new List<string>();
        var menu = TreeWalker.ControlViewWalker.GetParent(anyEntry);
        return menu.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem)).Cast<AutomationElement>()
            .Where(x => !x.Current.IsOffscreen).OrderBy(x => x.Current.BoundingRectangle.Top).Select(x => x.Current.Name).ToList();
    }

    // Every menu entry shown in the test host's windows (a popup is a window of its own), top to bottom.
    private static List<string> ShownMenuNames(IntPtr window)
    {
        var menuItem = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem);
        var roots = new List<AutomationElement> { AutomationElement.FromHandle(window) };
        roots.AddRange(AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, hostPid)).Cast<AutomationElement>());
        return roots.SelectMany(r => r.FindAll(TreeScope.Descendants, menuItem).Cast<AutomationElement>())
            .Where(x => !x.Current.IsOffscreen && x.Current.BoundingRectangle.Height > 0)
            .GroupBy(x => x.Current.Name).Select(g => g.First())
            .OrderBy(x => x.Current.BoundingRectangle.Top).Select(x => x.Current.Name).ToList();
    }
}
