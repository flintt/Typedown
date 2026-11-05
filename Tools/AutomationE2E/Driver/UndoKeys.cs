using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// KY01: Ctrl+Z and Ctrl+Y are the host's undo and redo, however close Ctrl and the letter come. The keyboard hook read
/// Ctrl as the web view's thread had it: a chord sent at once (a fast hand, a busy page, a macro key) was taken for a
/// plain Z, and the page made its own undo (the browser's in the visual editor, CodeMirror's in source mode), apart from
/// the host's history. (The modifiers are now the system's, so a Ctrl let go in another window cannot be still down for
/// the host either; a step pressing Ctrl here and letting it go in a second window left that window with the keyboard and
/// took the next cases' keys with it, so it is not part of this case.)
/// </summary>
internal static partial class Program
{

    private static async Task KY01(List<string> notes)
    {
        using var c = await Session("e2e KY01");
        var id = await Open(c, Fixture("ky01.md", "# KY01\n\nSome text.\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        // What the page's own keydown listeners and its history saw: a key the host took never reaches it.
        const string install = "(() => { window.__ky = []; if (!window.__kyOn) { window.__kyOn = 1; " +
            "document.addEventListener('keydown', e => window.__ky.push('kd:' + e.key), true); " +
            "document.addEventListener('beforeinput', e => { if (e.inputType.startsWith('history')) window.__ky.push('bi:' + e.inputType) }, true); } return 'ok' })()";
        async Task<string> Text() => (string?)(await Get(c, id))["text"] ?? "";
        async Task<string> PageSaw() => (string?)(await c.Call("test.editor.eval", new { windowId, script = "JSON.stringify(window.__ky)" }))["result"] ?? "";
        async Task Ready()
        {
            await Activate(window);
            await c.Call("test.editor.focus", new { windowId });
            await Task.Delay(200);
            await c.Call("test.editor.eval", new { windowId, script = install });
        }
        try
        {
            foreach (var (mode, letter) in new[] { ("visual", 'Q'), ("source", 'W') })
            {
                await c.Call("window.setView", new { windowId, mode });
                await Task.Delay(1000);
                await TypeInto(c, id, letter.ToString());
                await WaitForPage(c, id, t => t.Contains(letter), "the keystroke");
                await Task.Delay(500);
                // Ctrl and Z in one go, as a tool sends them.
                await Ready();
                Keys(("ctrl", (ushort)0x5A));
                await Task.Delay(1000);
                var undoSaw = await PageSaw();
                var undone = await Text();
                notes.Add($"{mode}: Ctrl+Z at once - page saw {undoSaw}; text '{undone.Replace("\n", "\\n")}'");
                Check(!undoSaw.Contains("kd:z"), $"{mode}: the host takes Ctrl+Z sent at once (the page saw {undoSaw})");
                Check(!undone.Contains(letter), $"{mode}: and undoes the letter");
                await Ready();
                Keys(("ctrl", (ushort)0x59));
                await Task.Delay(1000);
                var redoSaw = await PageSaw();
                var redone = await Text();
                notes.Add($"{mode}: Ctrl+Y at once - page saw {redoSaw}; text '{redone.Replace("\n", "\\n")}'");
                Check(!redoSaw.Contains("kd:y"), $"{mode}: the host takes Ctrl+Y sent at once (the page saw {redoSaw})");
                Check(redone.Contains(letter), $"{mode}: and its redo brings the letter back, from the same history");
            }
        }
        finally
        {
            try { await c.Call("window.setView", new { windowId, mode = "visual" }); } catch { }
        }
    }

    // The user's steps (2026-11-05, #39), with real keys and within the pause that ends a typing step: a paragraph typed,
    // selected (Ctrl+A in the visual editor selects the paragraph; Shift+Home in source mode), Enter over it. One Ctrl+Z
    // put back the state before the typing, or part of the paragraph: the typing had not yet become a step, and the
    // Enter changed it instead. Now one Ctrl+Z gives the paragraph back whole, a second takes the typing away, and Ctrl+Y
    // goes forward through both.
    private static async Task UN01(List<string> notes)
    {
        using var c = await Session("e2e UN01");
        var id = await Open(c, Fixture("un01.md", "# UN01\n\nfirst\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        async Task<string> Text() => ((string?)(await Get(c, id))["text"] ?? "").Replace("\r\n", "\n");
        async Task<string> Until(Func<string, bool> done, string what)
        {
            var text = "";
            if (!await Eventually(async () => done(text = await Text()), 5000)) throw new CaseFailed($"{what} (the text: '{text.Replace("\n", "\\n")}')");
            return text;
        }
        try
        {
            foreach (var (mode, select) in new[] { ("visual", "Ctrl+A"), ("source", "Shift+Home") })
            {
                await c.Call("window.setView", new { windowId, mode });
                await Task.Delay(1000);
                await Activate(window);
                await c.Call("test.editor.focus", new { windowId });
                await Task.Delay(200);
                Chord("Ctrl+End");
                Chord("Enter");
                TypeText("hello world");
                var typed = await Until(t => t.Contains("hello world"), $"{mode}: the paragraph typed");
                Chord(select);
                await Task.Delay(150);
                Chord("Enter");
                var cleared = await Until(t => !t.Contains("hello"), $"{mode}: Enter over the selection clears it");
                Chord("Ctrl+Z");
                var once = await Until(t => t != cleared, $"{mode}: Ctrl+Z changes the text");
                notes.Add($"{mode}: typed '{typed.Replace("\n", "\\n")}', Enter '{cleared.Replace("\n", "\\n")}', Ctrl+Z '{once.Replace("\n", "\\n")}'");
                Check(once.Contains("\nhello world\n"), $"{mode}: one Ctrl+Z after Enter over the selection gives the paragraph back whole");
                Chord("Ctrl+Z");
                var twice = await Until(t => t != once, $"{mode}: a second Ctrl+Z changes the text");
                notes.Add($"{mode}: a second Ctrl+Z '{twice.Replace("\n", "\\n")}'");
                Check(!twice.Contains("hello world"), $"{mode}: a second Ctrl+Z takes the typing away");
                Chord("Ctrl+Y");
                await Until(t => t.Contains("\nhello world\n"), $"{mode}: Ctrl+Y brings the paragraph back");
                Chord("Ctrl+Y");
                await Until(t => !t.Contains("hello"), $"{mode}: and a second Ctrl+Y the Enter");
                // The document as it began, for the other mode.
                await c.Call("document.replace", new { documentId = id, text = "# UN01\n\nfirst\n", baseRevision = await Revision(c, id) });
                await Task.Delay(800);
            }
        }
        finally
        {
            try { await c.Call("window.setView", new { windowId, mode = "visual" }); } catch { }
        }
    }
}
