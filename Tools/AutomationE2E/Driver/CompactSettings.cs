using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;

/// <summary>
/// CM01: in compact mode the settings line up with the main page and leaving them keeps the editor page. The main page
/// has no caption there (its menu bar is the top row); the settings had a 32 px caption, so everything below moved by
/// 8 px on the way in and back. Leaving the settings also loaded the editor page again - the whole document drawn once
/// more over a light page - which a mark left in the page shows: it is still there afterwards.
/// </summary>
internal static partial class Program
{
    private static async Task CM01(List<string> notes)
    {
        using var c = await Session("e2e CM01");
        var id = await Open(c, Fixture("cm01.md", "# CM01\n\nText\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var compactBefore = (bool)(await c.Call("test.settings.get", new { windowId, name = "AppCompactMode" }))["value"]!;
        AutomationElement Root() => AutomationElement.FromHandle(window);
        double? MenuBarBottom() => Deep.All(Root(), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuBar))
            .Cast<AutomationElement>().Select(e => (double?)e.Current.BoundingRectangle.Bottom).FirstOrDefault();
        double? SettingsTop() => Deep.All(Root(), new PropertyCondition(AutomationElement.AutomationIdProperty, "NavigationView"))
            .Cast<AutomationElement>().Select(e => (double?)e.Current.BoundingRectangle.Top).FirstOrDefault();
        try
        {
            await c.Call("test.settings.set", new { windowId, name = "AppCompactMode", value = true });
            await TypeInto(c, id, "");
            await Task.Delay(1500);
            await c.Call("test.editor.eval", new { windowId, script = "window.__cm01 = 'kept'; 'ok'" });
            var menuBottom = MenuBarBottom();
            await CtrlComma();
            await Task.Delay(2000);
            var settingsTop = SettingsTop();
            await CtrlComma();
            await Task.Delay(2000);
            var menuBottomAfter = MenuBarBottom();
            var mark = (string?)(await c.Call("test.editor.eval", new { windowId, script = "String(window.__cm01)" }))["result"];
            notes.Add($"menu bar bottom {menuBottom}, the settings' top {settingsTop}, menu bar bottom after {menuBottomAfter}; page mark {mark}");
            Check(menuBottom != null && settingsTop != null, "the menu bar and the settings were found");
            Check(Math.Abs((menuBottom ?? 0) - (settingsTop ?? -100)) <= 1, $"the settings start where the main page's top row ends ({settingsTop} vs {menuBottom})");
            Check(menuBottomAfter != null && Math.Abs(menuBottomAfter.Value - (menuBottom ?? 0)) <= 1, "back from the settings, the menu bar is where it was");
            Check(mark == "kept", $"leaving the settings kept the editor page (mark {mark})");
            // And the document is still there to type into.
            TypeChar('Q');
            await WaitForPage(c, id, t => t.Contains('Q'), "a letter after the settings");
        }
        finally
        {
            try { await c.Call("test.settings.set", new { windowId, name = "AppCompactMode", value = compactBefore }); } catch { }
            try
            {
                for (var i = 0; i < 3 && ((string?)(await Get(c, id))["text"] ?? "").Contains('Q'); i++)
                    await c.Call("document.undo", new { documentId = id, baseRevision = await Revision(c, id), reveal = "document" });
                await c.Call("document.close", new { documentId = id });
            }
            catch (Exception e) { notes.Add("cleanup: " + e.Message.Split('\n')[0]); }
        }
    }
}
