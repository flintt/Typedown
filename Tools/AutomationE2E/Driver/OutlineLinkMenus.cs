using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using Newtonsoft.Json.Linq;

/// <summary>
/// XM04: the menus of the outline and of a link, by real right-clicks. A heading's row in the outline: Copy link to
/// heading copies "[Beta](#beta)", the anchor a click on that link goes by; Collapse all folds the outline to its top
/// headings, Expand all opens it again. A link in the text: Edit link chooses its address, and what is typed then
/// replaces the address, the link's text kept.
/// </summary>
internal static partial class Program
{
    private static async Task XM04(List<string> notes)
    {
        using var c = await Session("e2e XM04");
        var id = await Open(c, Fixture("xm04.md", "# Alpha\n\nText.\n\n## Beta\n\nSee [the site](https://example.com/old) here.\n\n### Gamma\n\nEnd.\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        AutomationElement? Row(string name) => Deep.All(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.NameProperty, name))
            .Cast<AutomationElement>().FirstOrDefault(e => e.Current.BoundingRectangle.Width > 0 && e.Current.ControlType == ControlType.TreeItem)
            ?? Deep.All(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.NameProperty, name)).Cast<AutomationElement>().FirstOrDefault(e => e.Current.BoundingRectangle.Width > 0 && e.Current.BoundingRectangle.Left < 400);
        async Task MenuEntry(string automationId, string what)
        {
            AutomationElement? item = null;
            Check(await Eventually(() => Task.FromResult((item = FindAnywhereInHost(window, automationId)) != null), 4000, 150), $"the menu has {what}");
            await ClickInPopup(window, item!);
            await Task.Delay(500);
        }
        try
        {
            await c.Call("window.setView", new { windowId, mode = "visual", sidePane = new { open = true, page = "outline" } });
            await Task.Delay(1500);

            // Copy link to heading, on Beta's row.
            SetClipboardText("before");
            var beta = Row("Beta");
            Check(beta != null, "Beta's row is in the outline");
            await Click(window, beta!, Mouse.Right);
            await MenuEntry("CopyHeadingLinkItem", "Copy link to heading");
            string? copied = null;
            await Eventually(() => Task.FromResult((copied = ClipboardText()) != "before"), 3000, 150);
            notes.Add("copied: " + copied);
            Check(copied == "[Beta](#beta)", $"the heading's link copied ({copied})");

            // Collapse all: Gamma (under Beta) no longer shown; Expand all: back.
            await Click(window, Row("Alpha")!, Mouse.Right);
            await MenuEntry("TocCollapseAllItem", "Collapse all");
            Check(await Eventually(() => Task.FromResult(Row("Gamma") == null && Row("Beta") == null), 3000, 150), "Collapse all folds the outline to its top headings");
            await Click(window, Row("Alpha")!, Mouse.Right);
            await MenuEntry("TocExpandAllItem", "Expand all");
            Check(await Eventually(() => Task.FromResult(Row("Gamma") != null), 3000, 150), "Expand all opens it again");

            // Edit link: the address chosen, typed over.
            var at = (await c.Call("test.editor.eval", new { windowId, script = "(() => { const a = [...document.querySelectorAll('#ag-editor-id a, #ag-editor-id span')].find(e => e.textContent === 'the site'); if (!a) return null; a.scrollIntoView({ block: 'center' }); const r = a.getBoundingClientRect(); return { x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) } })()" }))["result"]!;
            Check(at.Type == JTokenType.Object, "the link is drawn in the editor");
            await Task.Delay(300);
            var screen = await c.Call("test.editor.screenPoint", new { windowId, x = (int)at["x"]!, y = (int)at["y"]! });
            GetWindowRect(window, out var rc);
            await Click(window, (int)screen["x"]! - rc.Left, (int)screen["y"]! - rc.Top, Mouse.Right);
            await MenuEntry("EditLinkItem", "Edit link");
            await Task.Delay(300);
            Send("https://example.org/new".SelectMany(ch => new[]
            {
                new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = 0x0004 } } },
                new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = 0x0004 | 0x0002 } } },
            }).ToArray());
            string text = "";
            var replaced = await Eventually(async () => (text = (string)(await Get(c, id))["text"]!).Contains("[the site](https://example.org/new)"), 5000, 200);
            notes.Add("after Edit link: " + text.Split('\n').FirstOrDefault(l => l.Contains("the site")));
            Check(replaced && !text.Contains("example.com/old"), "Edit link chooses the address: what is typed replaces it, the link's text kept");
        }
        finally
        {
            await CloseMenus();
            try { await c.Call("document.save", new { documentId = id }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
