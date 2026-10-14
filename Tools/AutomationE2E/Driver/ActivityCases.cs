using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// AC01: what an edition hears of around the writing (EditionHooks.Activity): another document shown, the mode changed,
/// an undo, the window left and come back to, a document saved under a new name.
/// </summary>
internal static partial class Program
{
    private static async Task AC01(List<string> notes)
    {
        using var c = await Session("e2e AC01");
        var first = await Open(c, Fixture("ac01-a.md", "# AC01 a\n\nSome text.\n"));
        var windowId = await WindowIdOf(c, first);
        var window = await WindowOf(windowId, c);
        await c.Call("test.activity.recent", new { clear = true });
        string? other = null;
        try
        {
            var second = await Open(c, Fixture("ac01-b.md", "# AC01 b\n\nMore text.\n"));
            await c.Call("document.focus", new { documentId = first });
            await c.Call("window.setView", new { windowId, mode = "reading" });
            await c.Call("window.setView", new { windowId, mode = "visual" });
            await TypeInto(c, first, "Q");
            await WaitForPage(c, first, t => t.Contains('Q'), "the keystroke");
            await Task.Delay(500);
            await Activate(window);
            await c.Call("test.editor.focus", new { windowId });
            await Task.Delay(200);
            Keys(("ctrl", (ushort)0x5A));
            await Task.Delay(1000);
            notes.Add($"after Ctrl+Z: '{((string?)(await Get(c, first))["text"] ?? "").Replace("\n", "\\n")}'");
            async Task<int> Undos() => ((JArray)(await c.Call("test.activity.recent"))["events"]!).Count(e => (string?)e["kind"] == "Undone");
            notes.Add($"undo events after Ctrl+Z: {await Undos()}");
            await TypeInto(c, first, "W");
            await WaitForPage(c, first, t => t.Contains('W'), "the keystroke");
            await Task.Delay(500);
            await InvokeMenuBarItem(window, "UndoItem");
            await Task.Delay(1000);
            notes.Add($"undo events after Edit > Undo: {await Undos()}; text '{((string?)(await Get(c, first))["text"] ?? "").Replace("\n", "\\n")}'");
            // Another window over it, then back.
            other = (string)(await c.Call("test.window.open"))["windowId"]!;
            await Task.Delay(1500);
            await Activate(window);
            await Task.Delay(800);
            var renamed = Path.Combine(fixtures, "ac01-renamed.md");
            if (File.Exists(renamed)) File.Delete(renamed);
            await c.Call("test.file.saveAs", new { windowId, path = renamed });
            await Task.Delay(800);
            var events = ((JArray)(await c.Call("test.activity.recent"))["events"]!).Cast<JObject>().ToList();
            notes.Add("events: " + string.Join(", ", events.Select(e => $"{e["kind"]}({e["mode"]}{(e["oldPath"]?.Type == JTokenType.String ? ", from " + Path.GetFileName((string)e["oldPath"]!) : "")} {Path.GetFileName((string?)e["path"] ?? "untitled")})")));
            bool Has(string kind, Func<JObject, bool>? also = null) => events.Any(e => (string?)e["kind"] == kind && (also == null || also(e)));
            Check(Has("DocumentShown", e => ((string?)e["path"] ?? "").EndsWith("ac01-b.md")) && Has("DocumentShown", e => ((string?)e["path"] ?? "").EndsWith("ac01-a.md")), "a document opened and one switched back to are shown");
            Check(Has("ModeChanged", e => (string?)e["mode"] == "reading") && Has("ModeChanged", e => (string?)e["mode"] == "visual"), "into reading mode and out");
            Check(events.Count(e => (string?)e["kind"] == "Undone") >= 2, "an undo by Ctrl+Z and one by Edit > Undo");
            Check(Has("WindowDeactivated") && Has("WindowActivated"), "the window left and come back to");
            Check(Has("DocumentRenamed", e => ((string?)e["oldPath"] ?? "").EndsWith("ac01-a.md") && ((string?)e["path"] ?? "").EndsWith("ac01-renamed.md")), "saved under a new name, with the old one");
        }
        finally
        {
            if (other != null) try { PostMessage(await WindowOf(other, c), 0x0010, IntPtr.Zero, IntPtr.Zero); } catch { }
        }
    }
}
