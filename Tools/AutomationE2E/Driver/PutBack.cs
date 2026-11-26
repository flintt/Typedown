using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// XF03: a file put back to what this window wrote before its last save - as Synology Drive did on the user's hp88
/// (2026-11-26: it failed to read a save, and wrote the NAS's older copy over it; the reload asked about took the
/// reader's last line away for good). Saved twice, then the first save's text written over the file: the question says
/// the file went back, its default keeps the reader's text and writes it to the file again; with the asking off it is
/// still asked, not reloaded; and taking the older version, the newer text is one Ctrl+Z away.
/// </summary>
internal static partial class Program
{
    private static async Task XF03(List<string> notes)
    {
        using var c = await Session("e2e XF03");
        var path = Fixture("xf03.md", "# XF03\n\none\n");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        async Task<JToken> Setting(string name) => (await c.Call("test.settings.get", new { windowId, name }))["value"]!;
        async Task Set(string name, object value) => await c.Call("test.settings.set", new { windowId, name, value });
        async Task<string> TextOf() => (string)(await Get(c, id))["text"]!;
        async Task Save(string text)
        {
            await c.Call("document.replace", new { documentId = id, baseRevision = await Revision(c, id), text });
            await c.Call("document.save", new { documentId = id, baseRevision = await Revision(c, id) });
            await Task.Delay(1200);
        }
        var autoReload = await Setting("AutoReload");
        var askBefore = await Setting("AskBeforeReload");
        try
        {
            await Set("AutoReload", true);
            await Set("AskBeforeReload", true);
            await Task.Delay(500);
            await Save("# XF03\n\none\n\ntwo\n");
            var older = File.ReadAllText(path);
            await Save("# XF03\n\none\n\ntwo\n\nthree, the last line\n");

            // Put back: the file holds the first save's text again. Asked, and the default (Close) keeps what is here.
            File.WriteAllText(path, older);
            var (title, content) = await PressDialogButton(c, windowId, DialogButton.Close, 6000);
            notes.Add($"put back, asked: '{title}' / '{content}'");
            Check(content?.Contains("xf03.md") == true && content.Contains(':'), $"the question says the file went back, and to the save of when ({content})");
            await Task.Delay(1500);
            Check((await TextOf()).Contains("three, the last line"), "kept: the reader's text is still there");
            Check(File.ReadAllText(path).Contains("three, the last line"), "and written to the file again");

            // With the asking off: still asked - a file put back is never reloaded without a question.
            await Set("AskBeforeReload", false);
            await Task.Delay(300);
            File.WriteAllText(path, older);
            (title, content) = await PressDialogButton(c, windowId, DialogButton.Primary, 6000);
            notes.Add($"asking off, put back again: '{title}' - took the older version");
            await Task.Delay(1000);
            Check(!(await TextOf()).Contains("three, the last line"), "the older version taken when asked for");
            // ... and the newer text one Ctrl+Z away.
            await c.Call("document.undo", new { documentId = id, baseRevision = await Revision(c, id), reveal = "document" });
            await Task.Delay(800);
            Check((await TextOf()).Contains("three, the last line"), "taken back by an undo");
        }
        finally
        {
            try { await c.Call("test.dialog.press", new { windowId, button = "close" }); } catch { }
            try { await Set("AutoReload", autoReload); } catch { }
            try { await Set("AskBeforeReload", askBefore); } catch { }
            try { await c.Call("document.save", new { documentId = id, baseRevision = await Revision(c, id) }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch (Exception e) { notes.Add("cleanup: " + e.Message.Split('\n')[0]); }
        }
    }
}
