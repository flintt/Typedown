using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    /// <summary>
    /// KB01: shortcuts that could not be changed before, changed. Last used tab (Alt+0) and zoom in (View > Zoom in) given
    /// other keys answer those, and Alt+0 then does nothing; reset zoom answers its default. A key the editor page acts on
    /// (indent, Tab) is the picker's "better not to change": its warning shows, a key without a modifier (F2) is taken, the
    /// page indents on that key and Tab no longer does; "Restore default" in the picker puts Tab back. An ordinary
    /// shortcut's picker has no warning.
    /// </summary>
    private static async Task KB01(List<string> notes)
    {
        using var c = await Session("e2e KB01");
        var a = await Open(c, Fixture("kb01-a.md", "# KB01 A\n"));
        var windowId = await WindowIdOf(c, a);
        var window = await WindowOf(windowId, c);
        var b = await Open(c, Fixture("kb01-b.md", "# KB01 B\n\n- one\n- two\n"));
        async Task<JToken> Setting(string name) => (await c.Call("test.settings.get", new { windowId, name }))["value"]!;
        async Task Set(string name, object? value) => await c.Call("test.settings.set", new { windowId, name, value });
        async Task<string> Active() => (string)((JArray)(await c.Call("window.list"))["windows"]!).First(w => (string)w["windowId"]! == windowId)["activeDocumentId"]!;
        async Task<string> WaitActive(Func<string, bool> done)
        {
            var now = await Active();
            for (var i = 0; i < 30 && !done(now); i++) { await Task.Delay(100); now = await Active(); }
            return now;
        }
        async Task<string> Section() => (string?)(await c.Call("test.settings.layout", new { windowId }))["section"] ?? "";
        // The settings page left: the menu bar made again, its shortcuts registered with the keys set now.
        async Task LeaveSettings()
        {
            if (await Section() != "") await c.Call("test.window.navigate", new { windowId, route = "Settings/General" });
            await Eventually(async () => await Section() == "", 4000);
            await Task.Delay(800);
        }
        async Task<string> Text(string id) => ((string?)(await Get(c, id))["text"] ?? "").Replace("\r", "");
        // A shortcut's row on the shortcuts page, found by its search box, and its picker opened.
        async Task<AutomationElement?> OpenPicker(string setting, string search)
        {
            var box = await WaitFor(window, "ShortcutSearchBox");
            var edit = box.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)) ?? box;
            ((ValuePattern)edit.GetCurrentPattern(ValuePattern.Pattern)).SetValue(search);
            await Task.Delay(600);
            Invoke(await WaitFor(window, "Shortcut." + setting));
            try { return await WaitFor(window, "WarningText", 3000); }
            catch (CaseFailed) { return null; }
        }
        var defaults = new Dictionary<string, JToken>();
        foreach (var name in new[] { "ShortcutLastUsedTab", "ShortcutZoomIn", "ShortcutIndent", "FontSize" }) defaults[name] = await Setting(name);
        try
        {
            await Activate(window);
            // Last used tab and zoom in on other keys: Ctrl+Alt+F9 and Ctrl+Alt+F10.
            await c.Call("test.window.navigate", new { windowId, route = "Settings/Shortcut" });
            await Set("ShortcutLastUsedTab", new { Modifiers = 3, Key = 120 });
            await Set("ShortcutZoomIn", new { Modifiers = 3, Key = 121 });
            await LeaveSettings();
            await c.Call("document.focus", new { documentId = a });
            Check(await WaitActive(d => d == a) == a, "A shown");
            await c.Call("document.focus", new { documentId = b });
            Check(await WaitActive(d => d == b) == b, "B shown after A");
            await Activate(window);
            await c.Call("test.editor.focus", new { windowId });
            await Task.Delay(300);
            Chord("Alt+0");
            await Task.Delay(800);
            Check(await Active() == b, "Alt+0 given away does nothing (still on B)");
            Chord("Ctrl+Alt+F9");
            Check(await WaitActive(d => d == a) == a, "Ctrl+Alt+F9, given to Last used tab, goes back to A");
            Chord("Ctrl+Alt+F9");
            Check(await WaitActive(d => d == b) == b, "Ctrl+Alt+F9 again goes to B");

            var size = (double)await Setting("FontSize");
            Chord("Ctrl+Alt+F10");
            var bigger = size;
            await Eventually(async () => (bigger = (double)await Setting("FontSize")) > size, 3000);
            notes.Add($"font size {size}, after Ctrl+Alt+F10 {bigger}");
            Check(bigger > size, $"Ctrl+Alt+F10, given to Zoom in, makes the text larger ({size} -> {bigger})");
            Chord("Ctrl+Shift+0");
            Check(await Eventually(async () => (double)await Setting("FontSize") == 16, 3000), $"Ctrl+Shift+0 (Reset zoom) goes back to 16 ({await Setting("FontSize")})");
            await InvokeMenuBarItem(window, "ZoomInItem");
            Check(await Eventually(async () => (double)await Setting("FontSize") > 16, 3000), $"View > Zoom in makes the text larger ({await Setting("FontSize")})");

            // An ordinary shortcut's picker: no warning.
            await c.Call("test.window.navigate", new { windowId, route = "Settings/Shortcut" });
            Check(await Eventually(async () => await Section() == "Shortcut", 4000), "the shortcuts page shown");
            var none = await OpenPicker("ShortcutLastUsedTab", "上次使用");
            Check(none == null, "an ordinary shortcut's picker has no warning");
            await PressDialogButton(c, windowId, DialogButton.Close);

            // Indent, a key better not changed: the warning, and F2 alone taken (F8 and F9 are Focus and Typewriter mode;
            // F6, F7 and F10 the browser's own).
            var warning = await OpenPicker("ShortcutIndent", "缩进");
            Check(warning != null, "the indent shortcut's picker warns that it is better not changed");
            notes.Add("warning: " + warning?.Current.Name);
            Chord("F2");
            await Task.Delay(400);
            await PressDialogButton(c, windowId, DialogButton.Primary);
            var indent = await Setting("ShortcutIndent");
            notes.Add("indent set to " + indent.ToString(Newtonsoft.Json.Formatting.None));
            Check((int?)indent["Key"] == 113 && (int?)indent["Modifiers"] == 0, $"F2 without a modifier saved for indent ({indent.ToString(Newtonsoft.Json.Formatting.None)})");
            await LeaveSettings();

            // The page indents on F2, and Tab does nothing.
            await ClickEditorTextOnce(c, windowId, window, "two");
            await Task.Delay(300);
            Chord("Tab");
            await Task.Delay(600);
            var afterTab = await Text(b);
            Check(afterTab.Contains("\n- two"), $"Tab given away does not indent ({afterTab.Replace("\n", "\\n")})");
            Chord("F2");
            var afterF2 = afterTab;
            await Eventually(async () => !(afterF2 = await Text(b)).Contains("\n- two"), 3000);
            notes.Add("after F2: " + afterF2.Replace("\n", "\\n"));
            Check(!afterF2.Contains("\n- two") && afterF2.Contains("- two"), $"F2, given to indent, indents the list item ({afterF2.Replace("\n", "\\n")})");

            // Restore default in the picker: Tab again.
            await c.Call("test.window.navigate", new { windowId, route = "Settings/Shortcut" });
            Check(await Eventually(async () => await Section() == "Shortcut", 4000), "the shortcuts page shown again");
            await OpenPicker("ShortcutIndent", "缩进");
            Invoke(await WaitFor(window, "RestoreDefaultButton"));
            await Task.Delay(300);
            await PressDialogButton(c, windowId, DialogButton.Primary);
            var restored = await Setting("ShortcutIndent");
            Check((int?)restored["Key"] == 9 && (int?)restored["Modifiers"] == 0, $"Restore default puts Tab back ({restored.ToString(Newtonsoft.Json.Formatting.None)})");
        }
        finally
        {
            try { await LeaveSettings(); } catch { }
            foreach (var (name, value) in defaults)
                try { await Set(name, value); } catch (Exception e) { notes.Add($"{name} not put back: {e.Message.Split('\n')[0]}"); }
            // The menu bar made again with the keys put back.
            try { await c.Call("test.window.navigate", new { windowId, route = "Settings/Shortcut" }); await Task.Delay(800); await LeaveSettings(); } catch { }
        }
    }
}
