using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// ED01: the keys that select and move in the visual editor reach it through the app's keyboard hook as Windows sends
/// them. Store reviews (1.x): "Ctrl+arrow keys cannot select a paragraph, only the mouse can", "Ctrl+F often does
/// nothing", "the mouse wheel does not scroll". The page side passes headless (Chrome on Linux); what only Windows
/// can show is whether the host lets these keys through, or takes them, and whether the wheel reaches the page.
/// ED02: a line break inside a table cell from the keyboard - Shift+Enter, and Alt+Enter as in a spreadsheet (a review
/// asked for it). Alt goes through the hook and the window's menu keys, so this is checked where those exist.
/// </summary>
internal static partial class Program
{
    private static async Task ED01(List<string> notes)
    {
        using var c = await Session("e2e ED01");
        var filler = string.Join("\n\n", Enumerable.Range(1, 80).Select(i => $"filler paragraph {i}"));
        var id = await Open(c, Fixture("ed01.md", $"# ED01\n\nfirst para has several words\n\nsecond para\n\n{filler}\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        async Task<string> Selected() => (string?)(await c.Call("test.editor.eval", new { windowId, script = "getSelection().toString()" }))["result"] ?? "";
        async Task<double> ScrollY() => (double?)(await c.Call("test.editor.eval", new { windowId, script = "window.scrollY" }))["result"] ?? -1;

        await WaitUntilDrawn(c, windowId, id, "first para");
        await ClickEditorTextOnce(c, windowId, window, "first para");
        await Task.Delay(300);
        Chord("Home");
        await Task.Delay(200);

        // Word by word: Ctrl+Shift+Right twice selects "first para " (Windows takes the space after a word with it).
        Chord("Ctrl+Shift+Right");
        await Task.Delay(150);
        Chord("Ctrl+Shift+Right");
        await Task.Delay(300);
        var words = await Selected();
        notes.Add($"after Ctrl+Shift+Right twice: '{words}'");
        Check(words.Trim() == "first para", $"Ctrl+Shift+Right selects word by word ('{words}')");

        // To the end of the paragraph: Ctrl+Shift+Down.
        Chord("Ctrl+Shift+Down");
        await Task.Delay(300);
        var paragraph = await Selected();
        notes.Add($"after Ctrl+Shift+Down: '{paragraph.Replace("\n", "\\n")}'");
        Check(paragraph.StartsWith("first para has several words"), $"Ctrl+Shift+Down extends the selection over the paragraph ('{paragraph.Replace("\n", "\\n")}')");

        // Ctrl+Left / Ctrl+Right move the caret by words without selecting.
        Chord("Ctrl+Left");
        await Task.Delay(150);
        Chord("Ctrl+Right");
        await Task.Delay(300);
        var afterMove = await Selected();
        Check(afterMove == "", $"Ctrl+Left and Ctrl+Right move without selecting ('{afterMove}')");

        // Ctrl+F opens the find bar with the keyboard in its search box; Esc closes it.
        Chord("Ctrl+F");
        var found = false;
        for (var i = 0; i < 40 && !found; i++)
        {
            found = FocusedAutomationId() == "TextBoxSearch";
            if (!found) await Task.Delay(50);
        }
        Check(found, "Ctrl+F in the editor opens the find bar with the keyboard in its search box");
        Chord("Esc");
        await Task.Delay(600);

        // The wheel over the page scrolls it, down and back up.
        var at = await c.Call("test.editor.screenPoint", new { windowId, x = 400, y = 300 });
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        var top = await ScrollY();
        for (var i = 0; i < 3; i++) { Wheel((int)at["x"]!, (int)at["y"]!, -120); await Task.Delay(120); }
        await Task.Delay(600);
        var down = await ScrollY();
        for (var i = 0; i < 3; i++) { Wheel((int)at["x"]!, (int)at["y"]!, 120); await Task.Delay(120); }
        await Task.Delay(600);
        var back = await ScrollY();
        notes.Add($"wheel: scrollY {top} -> {down} -> {back}");
        Check(down > top + 50, $"three notches down scroll the page ({top} -> {down})");
        Check(back < down - 50, $"three notches up scroll it back ({down} -> {back})");
    }

    private static async Task ED02(List<string> notes)
    {
        using var c = await Session("e2e ED02");
        var id = await Open(c, Fixture("ed02.md", "# ED02\n\n| A | B |\n| --- | --- |\n| abc | d |\n| efg | h |\n\nafter\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        await WaitUntilDrawn(c, windowId, id, "efg");
        foreach (var (cell, modifier, typed) in new[] { ("abc", "Shift", "one"), ("efg", "Alt", "two") })
        {
            await ClickEditorTextOnce(c, windowId, window, cell);
            await Task.Delay(300);
            Chord("End");
            await Task.Delay(150);
            Chord($"{modifier}+Enter");
            await Task.Delay(200);
            Keys(new Typed(typed));
            var text = "";
            var ok = await Eventually(async () => (text = (string?)(await Get(c, id))["text"] ?? "").Contains($"| {cell}<br/>{typed} |"), 3000);
            notes.Add($"{modifier}+Enter in '{cell}': " + text.Replace("\n", "\\n"));
            Check(ok, $"{modifier}+Enter in a table cell puts a line break in that cell and the typing stays there");
        }
    }

    /// <summary>
    /// The test PC's Chinese IME (Microsoft Pinyin, Chinese mode) typing into the visual editor through the keyboard, as
    /// a person types - keys, not characters sent past the IME as the other cases send them. Pinyin is composed and
    /// committed once; full-width brackets typed come out one each, with no closing one put in by the editor (it pairs
    /// only ASCII marks, on an input event, and what the IME commits comes as a composition); and what the host holds
    /// afterwards is what the editor holds. What the IME gives for `, Shift+8 and Shift+4 goes into the notes.
    /// </summary>
    private static async Task IM01(List<string> notes)
    {
        using var c = await Session("e2e IM01");
        var id = await Open(c, Fixture("im01.md", "# IM01\n\nstart\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        await WaitUntilDrawn(c, windowId, id, "start");
        await ClickEditorTextOnce(c, windowId, window, "start");
        await Task.Delay(300);
        Chord("End");
        await Task.Delay(150);
        async Task<string> Text() => ((string?)(await Get(c, id))["text"] ?? "").Replace("\r", "");
        async Task<string> Held() => (string?)(await c.Call("test.editor.eval", new { windowId, script = "window.__typedownMuya.getMarkdown()" }))["result"] ?? "";

        // n i h a o, Space: the first candidate committed.
        foreach (var k in "nihao") { Chord(k.ToString()); await Task.Delay(80); }
        Chord("Space");
        await Task.Delay(600);
        var text = "";
        await Eventually(async () => (text = await Text()).Contains("你好"), 3000);
        notes.Add("n i h a o Space: " + text.Replace("\n", "\\n"));
        if (!text.Contains("你好"))
            throw new CaseFailed($"the IME did not compose 你好 - not Microsoft Pinyin in Chinese mode on this PC? (an environment problem, not the editor's): {text.Replace("\n", "\\n")}");
        Check(text.Split("你好").Length == 2 && !text.Contains("nihao"), "the pinyin is committed once, with no letters of it left");

        // Shift+9, Shift+0: the IME's full-width brackets, one each.
        Chord("Shift+9"); await Task.Delay(250);
        Chord("Shift+0"); await Task.Delay(400);
        await Eventually(async () => (text = await Text()).Contains("）") || text.Contains(")"), 2000);
        notes.Add("Shift+9 Shift+0: " + text.Replace("\n", "\\n"));
        var open = text.Count(ch => ch == '（' || ch == '('); var close = text.Count(ch => ch == '）' || ch == ')');
        Check(open == 1 && close == 1, $"a bracket typed and its closing one typed: one of each, none put in by the editor ({open} and {close})");

        // `, Shift+8, Shift+4: whatever the IME gives, written down; nothing doubled by the editor around a composition.
        foreach (var chord in new[] { "Backquote", "Shift+8", "Shift+4" })
        {
            var before = await Text();
            Chord(chord); await Task.Delay(400);
            var after = await Text();
            notes.Add($"{chord}: {AddedText(before, after)}");
        }
        Chord("Space"); await Task.Delay(500);
        var host = await Text();
        var held = await Held();
        notes.Add("at the end: " + host.Replace("\n", "\\n"));
        Check(await Eventually(async () => (host = await Text()) == (held = await Held()), 3000), $"the host holds what the editor holds after the IME's input (host {host.Replace("\n", "\\n")}, editor {held.Replace("\n", "\\n")})");
    }

    /// <summary>What a key added to the document's text, as a short note.</summary>
    private static string AddedText(string before, string after) =>
        after.StartsWith(before.TrimEnd('\n')) ? Newtonsoft.Json.JsonConvert.ToString(after.Substring(before.TrimEnd('\n').Length).TrimEnd('\n')) : Newtonsoft.Json.JsonConvert.ToString(after.Replace("\n", "\\n"));

    /// <summary>
    /// Waits for the opened document to be on the page, drawn as well as loaded, before a case clicks into it: the text
    /// first reaches the page's Markdown, then the editor draws the block. Clicking at once failed now and then with
    /// "no paragraph with 'first para' on the page" while the document was still being shown.
    /// </summary>
    private static async Task WaitUntilDrawn(Client c, string windowId, string documentId, string text)
    {
        await WaitForPage(c, documentId, t => t.Contains(text), "the opened document");
        var script = "[...document.querySelectorAll('#ag-editor-id p, #ag-editor-id h1, #ag-editor-id td, #ag-editor-id span.ag-paragraph')].some(x => x.textContent.includes(" + Newtonsoft.Json.JsonConvert.ToString(text) + "))";
        var drawn = await Eventually(async () => (bool?)(await c.Call("test.editor.eval", new { windowId, script }))["result"] == true, 10000);
        if (!drawn) throw new CaseFailed($"'{text}' was never drawn on the page; screen: {Screenshot("not-drawn-" + DateTime.Now.ToString("HHmmss"))}");
        await Task.Delay(300);
    }
}
