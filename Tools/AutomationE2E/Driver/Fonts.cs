using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UIA = System.Windows.Automation;

/// <summary>
/// FL02: the font picker beside it (Settings > Editor > Font, Choose…): every installed font listed without typing,
/// narrowed down by its box, and the one clicked goes first in the setting with the fallbacks after it kept.
/// FL01: Settings > Editor > font family suggests the installed fonts as one types (Win2D enumerates them: in the
/// trimmed, unpackaged build Win2D must still load). "Seg" offers Segoe UI, which every Windows has.
/// </summary>
internal static partial class Program
{
    private static async Task FL01(List<string> notes)
    {
        using var c = await Session("e2e FL01");
        var id = await Open(c, Fixture("fl01.md", "# FL01\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var before = (string?)(await c.Call("test.settings.get", new { windowId, name = "FontFamily" }))["value"];
        try
        {
            await c.Call("test.window.navigate", new { windowId, route = "Settings" });
            await Task.Delay(1500);
            await Activate(window);
            var root = UIA.AutomationElement.FromHandle(window);
            var nav = Deep.First(root, new UIA.PropertyCondition(UIA.AutomationElement.AutomationIdProperty, "SettingsNavEditor")) ?? throw new CaseFailed("no Editor item on the settings page");
            if (nav.TryGetCurrentPattern(UIA.SelectionItemPattern.Pattern, out var select)) ((UIA.SelectionItemPattern)select).Select();
            else ((UIA.InvokePattern)nav.GetCurrentPattern(UIA.InvokePattern.Pattern)).Invoke();
            await Task.Delay(1500);
            var box = Deep.First(root, new UIA.PropertyCondition(UIA.AutomationElement.AutomationIdProperty, "FontFamilyBox")) ?? throw new CaseFailed("no font family box on the Editor settings");
            var edit = Deep.First(box, new UIA.PropertyCondition(UIA.AutomationElement.ControlTypeProperty, UIA.ControlType.Edit)) ?? box;
            edit.SetFocus();
            await Task.Delay(300);
            // Ctrl+A then the letters, as typed: a suggestion list comes only from the user's input.
            Send(Key(0x11, false), Key(0x41, false), Key(0x41, true), Key(0x11, true));
            foreach (var ch in "Seg") { TypeChar(ch); await Task.Delay(80); }
            List<string> offered = new();
            for (var i = 0; i < 20 && !offered.Any(n => n.StartsWith("Segoe UI")); i++)
            {
                await Task.Delay(200);
                offered = UIA.AutomationElement.RootElement.FindAll(UIA.TreeScope.Children, new UIA.PropertyCondition(UIA.AutomationElement.ProcessIdProperty, hostPid))
                    .Cast<UIA.AutomationElement>()
                    .SelectMany(w => Deep.All(w, new UIA.PropertyCondition(UIA.AutomationElement.ControlTypeProperty, UIA.ControlType.ListItem)))
                    .Select(Deep.Name).Where(n => n.StartsWith("Seg")).Distinct().ToList();
            }
            notes.Add("offered: " + string.Join(", ", offered.Take(8)));
            Check(offered.Any(n => n.StartsWith("Segoe UI")), "typing \"Seg\" offers Segoe UI (the installed fonts are enumerated)");
        }
        finally
        {
            Send(Key(0x1B, false), Key(0x1B, true));
            await Task.Delay(300);
            await c.Call("test.window.navigate", new { windowId, route = "Main" });
            var after = (string?)(await c.Call("test.settings.get", new { windowId, name = "FontFamily" }))["value"];
            if (after != before) await c.Call("test.settings.set", new { windowId, name = "FontFamily", value = before });
        }
    }

    private static async Task FL02(List<string> notes)
    {
        using var c = await Session("e2e FL02");
        var id = await Open(c, Fixture("fl02.md", "# FL02\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var before = (string?)(await c.Call("test.settings.get", new { windowId, name = "FontFamily" }))["value"];
        UIA.AutomationElement? Anywhere(string automationId) =>
            UIA.AutomationElement.RootElement.FindAll(UIA.TreeScope.Children, new UIA.PropertyCondition(UIA.AutomationElement.ProcessIdProperty, hostPid))
                .Cast<UIA.AutomationElement>()
                .Select(w => Deep.First(w, new UIA.PropertyCondition(UIA.AutomationElement.AutomationIdProperty, automationId)))
                .FirstOrDefault(e => e != null);
        int Listed() => UIA.AutomationElement.RootElement.FindAll(UIA.TreeScope.Children, new UIA.PropertyCondition(UIA.AutomationElement.ProcessIdProperty, hostPid))
            .Cast<UIA.AutomationElement>()
            .Sum(w => Deep.All(w, new UIA.PropertyCondition(UIA.AutomationElement.ControlTypeProperty, UIA.ControlType.ListItem)).Count(e => (e.Current.AutomationId ?? "").StartsWith("FontPicker.Font:")));
        try
        {
            await c.Call("test.settings.set", new { windowId, name = "FontFamily", value = "Georgia, 'Microsoft YaHei UI', sans-serif" });
            await c.Call("test.window.navigate", new { windowId, route = "Settings" });
            await Task.Delay(1500);
            await Activate(window);
            var root = UIA.AutomationElement.FromHandle(window);
            var nav = Deep.First(root, new UIA.PropertyCondition(UIA.AutomationElement.AutomationIdProperty, "SettingsNavEditor")) ?? throw new CaseFailed("no Editor item on the settings page");
            if (nav.TryGetCurrentPattern(UIA.SelectionItemPattern.Pattern, out var select)) ((UIA.SelectionItemPattern)select).Select();
            else ((UIA.InvokePattern)nav.GetCurrentPattern(UIA.InvokePattern.Pattern)).Invoke();
            await Task.Delay(1500);
            var choose = Deep.First(root, new UIA.PropertyCondition(UIA.AutomationElement.AutomationIdProperty, "FontFamilyChoose")) ?? throw new CaseFailed("no Choose… button beside the font box");
            // The row may be below the page's fold in a short window: scrolled to first, as a reader would.
            if (choose.TryGetCurrentPattern(UIA.ScrollItemPattern.Pattern, out var scroll)) ((UIA.ScrollItemPattern)scroll).ScrollIntoView();
            await Task.Delay(400);
            await Click(window, choose);
            UIA.AutomationElement? search = null;
            for (var i = 0; i < 25 && search == null; i++) { await Task.Delay(200); search = Anywhere("FontPicker.Search"); }
            Check(search != null, "Choose… opens the list with its search box");
            var shown = 0;
            for (var i = 0; i < 15 && shown < 5; i++) { await Task.Delay(200); shown = Listed(); }
            notes.Add($"rows shown before typing: {shown}");
            Check(shown >= 5, "the fonts are listed without typing anything");
            foreach (var ch in "Segoe UI") { TypeChar(ch); await Task.Delay(60); }
            UIA.AutomationElement? segoe = null;
            for (var i = 0; i < 20 && segoe == null; i++) { await Task.Delay(200); segoe = Anywhere("FontPicker.Font:Segoe UI"); }
            Check(segoe != null, "typing narrows the list to Segoe UI");
            await Click(window, segoe!);
            string? after = null;
            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(200);
                after = (string?)(await c.Call("test.settings.get", new { windowId, name = "FontFamily" }))["value"];
                if (after != null && after.StartsWith("'Segoe UI'")) break;
            }
            notes.Add($"font after: {after}");
            Check(after == "'Segoe UI', 'Microsoft YaHei UI', sans-serif", "the font clicked goes first, the fallbacks after it stay");
            Check(Anywhere("FontPicker.Search") == null, "the list closes once a font is chosen");
        }
        finally
        {
            Send(Key(0x1B, false), Key(0x1B, true));
            await Task.Delay(300);
            await c.Call("test.window.navigate", new { windowId, route = "Main" });
            await c.Call("test.settings.set", new { windowId, name = "FontFamily", value = before ?? "" });
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
