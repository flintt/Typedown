using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Automation;
using Newtonsoft.Json.Linq;

/// <summary>
/// SK01 (the user's 33, 2026-11-22): the scratchpad, a small window of its own with one text box, the same for every
/// window and tab. Off by default: its key does nothing. On (Settings > General): View > Scratchpad (the real menu) shows
/// it over the other windows; what is typed in it stays when it is hidden and shown again (its key from the main window,
/// its key and Esc in it); the editor's Put in Scratchpad adds the selection at its end; its pin lets it go behind; the
/// text, the pin and its place are there after the app is started again. Without the system's frame and not in the
/// taskbar (the user, 2026-11-22): its own bar drags it, its edges size it, its x hides it.
/// </summary>
internal static partial class Program
{
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowStyle(IntPtr window, int index);

    private static async Task SK01(List<string> notes)
    {
        const string original = "# SK01\n\nkeep this line\n\nanother line\n";
        using var c = await Session("e2e SK01");
        var id = await Open(c, Fixture("sp01.md", original));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        async Task<JObject> State() => (JObject)await c.Call("test.scratchpad.state", new { windowId });
        async Task Front()
        {
            await Activate(window);
            await c.Call("test.editor.focus", new { windowId });
            await Task.Delay(300);
        }
        try
        {
            await c.Call("window.setView", new { windowId, mode = "visual", bounds = new { x = 40, y = 40, width = 1100, height = 760 } });
            await c.Call("test.scratchpad.set", new { windowId, text = "" });
            await Eventually(async () => await PageShows(c, windowId, "keep this line"), 15000, 200);

            // Off (the default): the key does nothing, the menu has no entry.
            Check((bool?)(await c.Call("test.settings.get", new { windowId, name = "ScratchpadEnabled" }))["value"] == false, "the scratchpad starts off");
            await Front();
            Chord("Ctrl+Shift+J");
            await Task.Delay(800);
            Check((bool?)(await State())["shown"] == false, "off: Ctrl+Shift+J shows nothing");

            // On: View > Scratchpad, the real menu.
            await c.Call("test.settings.set", new { windowId, name = "ScratchpadEnabled", value = true });
            await Task.Delay(300);
            await InvokeMenuBarItem(window, "ScratchpadItem");
            JObject s = new();
            await Eventually(async () => (bool?)(s = await State())["shown"] == true, 5000, 200);
            notes.Add("shown: " + s.ToString(Newtonsoft.Json.Formatting.None));
            Check((bool?)s["shown"] == true, "View > Scratchpad shows it");
            Check((bool?)s["topmost"] == true, "over the other windows by default");
            var pad = new IntPtr((long)s["hwnd"]!);
            Check(pad != IntPtr.Zero && pad != window, "a window of its own");

            // On the screen, with no title bar of the system's (its thick frame stays: the system sizes it by its edges), not
            // in the taskbar (nor Alt+Tab).
            await Task.Delay(600);
            s = await State();
            notes.Add("shot, borderless: " + Screenshot("SK01-0-borderless") + " " + s.ToString(Newtonsoft.Json.Formatting.None));
            Check((bool?)s["visible"] == true, "it is on the screen: " + s["bounds"]);
            const int WS_CAPTION = 0x00C00000;
            var style = GetWindowStyle(pad, -16);
            Check((style & WS_CAPTION) != WS_CAPTION, $"no title bar of the system's (style 0x{style:X8})");
            Check((bool?)s["inSwitchers"] == false, "not in the taskbar");

            // Its bar dragged by a real mouse moves it; its right and bottom edges dragged size it; all in physical pixels.
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            GetWindowRect(pad, out var r0);
            await Drag(pad, (r0.Left + 30, r0.Top + 12), (r0.Left + 30 - 120, r0.Top + 12 + 70));
            await Task.Delay(400);
            GetWindowRect(pad, out var r1);
            Check(Math.Abs(r1.Left - (r0.Left - 120)) <= 4 && Math.Abs(r1.Top - (r0.Top + 70)) <= 4 && r1.Right - r1.Left == r0.Right - r0.Left,
                $"its bar dragged moves it ({r0.Left},{r0.Top} -> {r1.Left},{r1.Top}, wanted {r0.Left - 120},{r0.Top + 70})");
            await Drag(pad, (r1.Right - 2, (r1.Top + r1.Bottom) / 2), (r1.Right - 2 + 80, (r1.Top + r1.Bottom) / 2));
            await Task.Delay(400);
            GetWindowRect(pad, out var r2);
            Check(Math.Abs((r2.Right - r2.Left) - (r1.Right - r1.Left + 80)) <= 6 && r2.Left == r1.Left, $"its right edge dragged makes it wider ({r1.Right - r1.Left} -> {r2.Right - r2.Left})");
            await Drag(pad, ((r2.Left + r2.Right) / 2, r2.Bottom - 2), ((r2.Left + r2.Right) / 2, r2.Bottom - 2 + 60));
            await Task.Delay(400);
            GetWindowRect(pad, out var r3);
            Check(Math.Abs((r3.Bottom - r3.Top) - (r2.Bottom - r2.Top + 60)) <= 6 && r3.Top == r2.Top, $"its bottom edge dragged makes it taller ({r2.Bottom - r2.Top} -> {r3.Bottom - r3.Top})");

            // In the custom theme's colours, at once (the user, 2026-11-22): Dracula's panels and text; none again, its own.
            var themeBefore = (string?)(await c.Call("test.settings.get", new { windowId, name = "CustomTheme" }))["value"];
            await c.Call("test.settings.set", new { windowId, name = "CustomTheme", value = "dracula" });
            await Eventually(async () => (string?)(await State())["background"] == "#343746", 4000, 200);
            s = await State();
            Check((string?)s["background"] == "#343746" && (string?)s["foreground"] == "#F8F8F2", $"under Dracula: its panels' colour and its text ({s["background"]}, {s["foreground"]})");
            notes.Add("shot, under Dracula: " + Screenshot("SK01-2-dracula"));
            await c.Call("test.settings.set", new { windowId, name = "CustomTheme", value = themeBefore ?? "" });
            await Eventually(async () => (string?)(await State())["background"] != "#343746", 4000, 200);
            Check((string?)(await State())["background"] != "#343746", "the theme taken off: its own colours again");

            // Its x hides it; its key shows it again.
            var close = FindIn(pad, "ScratchpadClose");
            Check(close != null, "its x");
            if (close != null)
            {
                var (cx, cy) = CenterOf(close);
                await Click(pad, cx, cy);
            }
            await Eventually(async () => (bool?)(await State())["shown"] == false, 4000, 200);
            Check((bool?)(await State())["shown"] == false, "its x hides it");
            await Front();
            Chord("Ctrl+Shift+J");
            await Eventually(async () => (bool?)(await State())["shown"] == true, 4000, 200);
            Check((bool?)(await State())["shown"] == true, "its key shows it again");

            // Typed in, as a person types.
            var box = FindIn(pad, "ScratchpadText");
            Check(box != null, "its text box");
            box?.SetFocus();
            await Task.Delay(200);
            TypeText("first note");
            await Eventually(async () => (string?)(await State())["text"] == "first note", 4000, 200);
            s = await State();
            Check((string?)s["text"] == "first note", "what is typed is its text: " + s["text"]);
            notes.Add("shot, the scratchpad over the window: " + Screenshot("SK01-1-shown"));

            // Its key in it hides it; the key in the main window shows it again, the text kept; Esc hides it.
            Chord("Ctrl+Shift+J");
            await Eventually(async () => (bool?)(await State())["shown"] == false, 4000, 200);
            Check((bool?)(await State())["shown"] == false, "Ctrl+Shift+J in it hides it");
            await Front();
            Chord("Ctrl+Shift+J");
            await Eventually(async () => (bool?)(await State())["shown"] == true, 4000, 200);
            s = await State();
            Check((bool?)s["shown"] == true && (string?)s["text"] == "first note", "Ctrl+Shift+J in the main window shows it again, the text kept: " + s.ToString(Newtonsoft.Json.Formatting.None));
            Chord("Esc");
            await Eventually(async () => (bool?)(await State())["shown"] == false, 4000, 200);
            Check((bool?)(await State())["shown"] == false, "Esc hides it");

            // Put in Scratchpad: the line selected as a person selects it, the editor's menu.
            await ClickEditorText(c, windowId, window, "keep this line");
            Chord("Home");
            Chord("Shift+End");
            await Task.Delay(400);
            var menu = await ContextMenuAt(c, windowId, window, "#ag-editor-id p", "PutInScratchpadItem");
            menu.TryGetValue("PutInScratchpadItem", out var put);
            Check(put != null && put.Current.IsEnabled, "the editor's menu has Put in Scratchpad (" + string.Join(", ", menu.Keys) + ")");
            if (put != null) Invoke(put);
            await Eventually(async () => ((string?)(await State())["text"])?.Contains("keep this line") == true, 5000, 200);
            s = await State();
            Check((string?)s["text"] == "first note\n\nkeep this line", "the selection added at the end, a blank line before it: " + s["text"]);
            Check((bool?)s["shown"] == true, "and the scratchpad shown");

            // Its pin let go: behind the other windows from then on, kept as a setting.
            var pin = FindIn(pad, "ScratchpadTopmost");
            Check(pin != null, "its pin");
            if (pin?.GetCurrentPattern(TogglePattern.Pattern) is TogglePattern toggle) toggle.Toggle();
            await Eventually(async () => (bool?)(await State())["topmost"] == false, 4000, 200);
            Check((bool?)(await State())["topmost"] == false, "its pin let go: not over the other windows");
            Check((bool?)(await c.Call("test.settings.get", new { windowId, name = "ScratchpadTopmost" }))["value"] == false, "kept as a setting");
            GetWindowRect(pad, out var before);
            notes.Add($"its place: {before.Left},{before.Top} {before.Right - before.Left}x{before.Bottom - before.Top}");
            Chord("Esc");
            await Task.Delay(800);

            // Started again: the text, the pin and its place as they were.
            await RestartHost(notes);
            using var again = await Session("e2e SK01 again");
            var w2 = await FirstWindowId(again);
            var main2 = await WindowOf(w2, again);
            await Activate(main2);
            await again.Call("test.editor.focus", new { windowId = w2 });
            await Task.Delay(300);
            Chord("Ctrl+Shift+J");
            JObject s2 = new();
            await Eventually(async () => (bool?)(s2 = (JObject)await again.Call("test.scratchpad.state", new { windowId = w2 }))["shown"] == true, 6000, 200);
            Check((string?)s2["text"] == "first note\n\nkeep this line", "after a restart: the text as it was: " + s2["text"]);
            Check((bool?)s2["topmost"] == false, "the pin as it was");
            GetWindowRect(new IntPtr((long)s2["hwnd"]!), out var after);
            Check(Math.Abs(after.Left - before.Left) <= 2 && Math.Abs(after.Top - before.Top) <= 2 && Math.Abs((after.Right - after.Left) - (before.Right - before.Left)) <= 2,
                $"its place as it was ({after.Left},{after.Top} {after.Right - after.Left}x{after.Bottom - after.Top})");
            Chord("Esc");
            await again.Call("test.scratchpad.set", new { windowId = w2, text = "" });
            await again.Call("test.settings.set", new { windowId = w2, name = "ScratchpadTopmost", value = true });
            await again.Call("test.settings.set", new { windowId = w2, name = "ScratchpadEnabled", value = false });
        }
        finally
        {
            try { await c.Call("test.settings.set", new { windowId, name = "ScratchpadEnabled", value = false }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
