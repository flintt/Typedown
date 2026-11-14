using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// NC01, NC02: File > New from Clipboard (the user, 2026-11-14). NC01 in the app: Alt+Shift+N pressed with real keys
/// makes a new tab, shown, with the clipboard's text; with no text on the clipboard it says so and makes none. NC02 from
/// another program: with Settings > General's global shortcut on, the key pressed while the window is minimized brings
/// it back with the clipboard's text in a new tab; off (the default), the key does nothing to it.
/// </summary>
internal static partial class Program
{
    private const ushort VkAlt = 0x12, VkShift = 0x10, VkN = 0x4E;

    private static void PressAltShiftN() =>
        Send(Key(VkAlt, false), Key(VkShift, false), Key(VkN, false), Key(VkN, true), Key(VkShift, true), Key(VkAlt, true));

    private static async Task NC01(List<string> notes)
    {
        var path = Fixture("nc01.md", "# NC01\n\nThe document shown before.\n");
        using var c = await Session("e2e NC01");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var made = new List<string>();
        try
        {
            await Eventually(async () => await PageShows(c, windowId, "The document shown before."), 15000, 200);
            const string clip = "# NC01 from the clipboard\n\n- one\n- two\n";
            SetClipboardText(clip);
            var before = await DocumentsOf(c, windowId);
            await Activate(window);
            await c.Call("test.editor.focus", new { windowId });
            await Task.Delay(300);
            PressAltShiftN();
            string? added = null;
            await Eventually(async () => (added = (await DocumentsOf(c, windowId)).Except(before).FirstOrDefault()) != null, 8000, 100);
            Check(added != null, "Alt+Shift+N makes a new tab");
            made.Add(added!);
            var text = "";
            await Eventually(async () => (text = ((string?)(await Get(c, added!))["text"] ?? "").Replace("\r\n", "\n")).Contains("NC01 from the clipboard"), 8000, 100);
            notes.Add("new tab's text: " + text.Replace("\n", "⏎"));
            Check(text.TrimEnd('\n') == clip.TrimEnd('\n'), $"the new tab has the clipboard's text ({text.Length} chars)");
            Check(await ActiveOf(c, windowId) == added, "the new tab is the one shown");
            Check(await PageShows(c, windowId, "NC01 from the clipboard"), "the page shows it");

            // In reading mode too, which takes no other paste: the new tab stayed empty there (the user, 2026-11-14).
            await c.Call("test.settings.set", new { windowId, name = "ReadOnly", value = true });
            await Task.Delay(500);
            const string readingClip = "# NC01 in reading mode\n\nShown, not edited.\n";
            SetClipboardText(readingClip);
            var beforeReading = await DocumentsOf(c, windowId);
            await Activate(window);
            await c.Call("test.editor.focus", new { windowId });
            await Task.Delay(300);
            PressAltShiftN();
            string? readingTab = null;
            await Eventually(async () => (readingTab = (await DocumentsOf(c, windowId)).Except(beforeReading).FirstOrDefault()) != null, 8000, 100);
            Check(readingTab != null, "reading mode: Alt+Shift+N makes a new tab");
            made.Add(readingTab!);
            var readingText = "";
            await Eventually(async () => (readingText = ((string?)(await Get(c, readingTab!))["text"] ?? "").Replace("\r\n", "\n")).Contains("NC01 in reading mode"), 8000, 100);
            notes.Add("reading mode, new tab's text: " + readingText.Replace("\n", "⏎"));
            Check(readingText.TrimEnd('\n') == readingClip.TrimEnd('\n'), $"reading mode: the new tab has the clipboard's text ({readingText.Length} chars)");
            Check(await PageShows(c, windowId, "Shown, not edited."), "reading mode: the page shows it");
            await c.Call("test.settings.set", new { windowId, name = "ReadOnly", value = false });

            // Nothing to paste: said so, no tab made.
            EmptyTheClipboard();
            var count = (await DocumentsOf(c, windowId)).Count;
            await Activate(window);
            await c.Call("test.editor.focus", new { windowId });
            await Task.Delay(300);
            PressAltShiftN();
            JArray? closed = null;
            await Eventually(async () => (closed = (JArray?)(await c.Call("test.dialogs.dismiss", new { }))["closed"]) is { Count: > 0 }, 5000, 200);
            notes.Add("no text: " + (closed == null ? "no dialog" : string.Join(", ", closed)));
            Check(closed is { Count: > 0 }, "with no text on the clipboard it says so");
            Check((await DocumentsOf(c, windowId)).Count == count, "and makes no tab");
        }
        finally
        {
            try { await c.Call("test.settings.set", new { windowId, name = "ReadOnly", value = false }); } catch { }
            foreach (var d in made) await UndoAndClose(c, d, notes);
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }

    private static async Task NC02(List<string> notes)
    {
        var path = Fixture("nc02.md", "# NC02\n\nThe document shown before.\n");
        using var c = await Session("e2e NC02");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var made = new List<string>();
        try
        {
            await Eventually(async () => await PageShows(c, windowId, "The document shown before."), 15000, 200);
            // Off (the default): the key pressed elsewhere does nothing to the app.
            await c.Call("test.settings.set", new { windowId, name = "GlobalNewFromClipboard", value = false });
            SetClipboardText("# NC02 off\n");
            var before = await DocumentsOf(c, windowId);
            ShowWindow(window, 6 /* SW_MINIMIZE */);
            await Task.Delay(1500);
            PressAltShiftN();
            await Task.Delay(2500);
            var offAdded = (await DocumentsOf(c, windowId)).Except(before).ToList();
            made.AddRange(offAdded);
            notes.Add($"off: {offAdded.Count} tab(s) made, window minimized {IsIconic(window)}");
            Check(offAdded.Count == 0 && IsIconic(window), "off, the key pressed in another program does nothing to the app");

            // On: the window back in front, the clipboard's text in a new tab.
            await c.Call("test.settings.set", new { windowId, name = "GlobalNewFromClipboard", value = true });
            await Task.Delay(800);
            const string clip = "# NC02 from another program\n\nPasted by the global shortcut.\n";
            SetClipboardText(clip);
            PressAltShiftN();
            string? added = null;
            await Eventually(async () => (added = (await DocumentsOf(c, windowId)).Except(before).FirstOrDefault()) != null, 10000, 100);
            Check(added != null, "on, the key pressed in another program makes a new tab");
            made.Add(added!);
            var text = "";
            await Eventually(async () => (text = ((string?)(await Get(c, added!))["text"] ?? "").Replace("\r\n", "\n")).Contains("NC02 from another program"), 8000, 100);
            await Eventually(async () => !IsIconic(window) && GetForegroundWindow() == window, 5000, 100);
            notes.Add($"on: text {text.Replace("\n", "⏎")}; minimized {IsIconic(window)}, in front {GetForegroundWindow() == window}");
            Check(text.TrimEnd('\n') == clip.TrimEnd('\n'), "the new tab has the clipboard's text");
            Check(!IsIconic(window) && GetForegroundWindow() == window, "the window is back, in front");
            Check(await ActiveOf(c, windowId) == added, "showing the new tab");
        }
        finally
        {
            try { await c.Call("test.settings.set", new { windowId, name = "GlobalNewFromClipboard", value = false }); } catch { }
            ShowWindow(window, 9 /* SW_RESTORE */);
            try { await Activate(window); } catch { }
            foreach (var d in made) await UndoAndClose(c, d, notes);
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
