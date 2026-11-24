using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

/// <summary>
/// PS03 (the user, 2026-11-24: the side pane's files and outline had no key): with the caret in the document, by real
/// keys - Ctrl+Shift+E opens the side pane at its files, Ctrl+Shift+O goes to its outline, Ctrl+Shift+O again closes the
/// pane, Ctrl+Shift+E opens it at the files again; a block folded away is opened out by its key. Both are in Settings >
/// Shortcuts.
/// </summary>
internal static partial class Program
{
    private static async Task PS03(List<string> notes)
    {
        using var c = await Session("e2e PS03");
        var path = Fixture($"ps03-{Guid.NewGuid().ToString("N").Substring(0, 8)}.md", "# PS03\n\n## A heading\n\nThe caret is here.\n");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        async Task<T> Get<T>(string name) => (await c.Call("test.settings.get", new { windowId, name }))["value"]!.ToObject<T>()!;
        async Task<string> Pane() => !await Get<bool>("SidePaneOpen") ? "closed" : await Get<int>("SidePaneIndex") == 1 ? "outline" : "files";
        var openBefore = await Get<bool>("SidePaneOpen");
        var pageBefore = await Get<int>("SidePaneIndex");
        async Task Press(string keys, string expected, string what)
        {
            await ClickEditorText(c, windowId, window, "The caret is here.");
            await Task.Delay(200);
            Chord(keys);
            await Eventually(async () => await Pane() == expected, 3000, 100);
            var now = await Pane();
            notes.Add($"{keys}: {now}");
            Check(now == expected, $"{what}: {now}");
        }
        try
        {
            await c.Call("window.setView", new { windowId, mode = "visual", sidePane = new { open = false } });
            await WaitUntilDrawn(c, windowId, id, "The caret is here.");
            await Press("Ctrl+Shift+E", "files", "Ctrl+Shift+E in the document opens the side pane at its files");
            await Press("Ctrl+Shift+O", "outline", "Ctrl+Shift+O goes to its outline");
            notes.Add("shot, the outline by its key: " + Screenshot("PS03-outline"));
            await Press("Ctrl+Shift+O", "closed", "Ctrl+Shift+O with the outline shown closes the pane");
            await Press("Ctrl+Shift+E", "files", "Ctrl+Shift+E opens it at the files again");
            // A block folded away is opened out by its key.
            await c.Call("test.settings.set", new { windowId, name = "SidePaneOutlineCollapsed", value = true });
            await Press("Ctrl+Shift+O", "outline", "Ctrl+Shift+O with the outline folded away goes to it");
            Check(!await Get<bool>("SidePaneOutlineCollapsed"), "and opens it out");
            var keys = $"{(await c.Call("test.settings.get", new { windowId, name = "ShortcutSidePaneFiles" }))["value"]} / {(await c.Call("test.settings.get", new { windowId, name = "ShortcutSidePaneOutline" }))["value"]}";
            notes.Add("their settings: " + keys);
        }
        finally
        {
            try { await c.Call("test.settings.set", new { windowId, name = "SidePaneIndex", value = pageBefore }); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "SidePaneOpen", value = openBefore }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
            try { File.Delete(path); } catch { }
        }
    }
}
