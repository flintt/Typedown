using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UIA = System.Windows.Automation;

/// <summary>
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
}
