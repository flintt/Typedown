using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;

/// <summary>
/// XM05 (the user, 2026-11-13; the menu report's M11): the context menu's format buttons show the format where it was
/// right-clicked - pressed in bold text for bold, in italic for italic, none in plain text; Bold pressed in bold text
/// takes the bold off, and the next menu there shows it off.
/// </summary>
internal static partial class Program
{
    private static async Task XM05(List<string> notes)
    {
        var folder = FixtureFolder("xm05");
        var path = Path.Combine(folder, "xm05.md");
        File.WriteAllText(path, "# Formats\n\nPlainwordsatthestart then **boldwordshere** then *italicwordshere* then <mark>markedwordshere</mark> end.\n");
        using var c = await Session("e2e XM05");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        await Task.Delay(1500);
        // Highlight too (Typedown PR #4, Alex Kay): a toggle as the others.
        string[] buttons = { "ContextFormatStrong", "ContextFormatEmphasis", "ContextFormatUnderline", "ContextFormatStrikethrough", "ContextFormatHighlight" };
        // Each format button's state as a screen reader has it (its toggle pattern).
        Dictionary<string, bool?> States()
        {
            var states = new Dictionary<string, bool?>();
            foreach (var b in buttons)
            {
                var e = FindAnywhereInHost(window, b);
                states[b] = e != null && e.TryGetCurrentPattern(TogglePattern.Pattern, out var p) ? ((TogglePattern)p).Current.ToggleState == ToggleState.On : null;
            }
            return states;
        }
        string Show(Dictionary<string, bool?> s) => string.Join(", ", s.Select(x => $"{x.Key.Replace("ContextFormat", "")}={(x.Value == null ? "none" : x.Value.Value ? "on" : "off")}"));
        void Close() => Send(Key(0x1B, false), Key(0x1B, true));
        async Task<Dictionary<string, bool?>> MenuOn(string where, string selector)
        {
            await Eventually(() => Task.FromResult(FindAnywhereInHost(window, "SelectAllItem") == null), 3000, 100);
            await ContextMenuAt(c, windowId, window, selector);
            await Task.Delay(400);
            var s = States();
            notes.Add($"{where}: {Show(s)}");
            return s;
        }
        try
        {
            var bold = await MenuOn("bold text", "#ag-editor-id strong");
            Close(); await Task.Delay(300);
            Check(bold["ContextFormatStrong"] == true && bold["ContextFormatEmphasis"] == false, $"in bold text Bold is pressed, Italic not ({Show(bold)})");

            var italic = await MenuOn("italic text", "#ag-editor-id em");
            Close(); await Task.Delay(300);
            Check(italic["ContextFormatEmphasis"] == true && italic["ContextFormatStrong"] == false, $"in italic text Italic is pressed, Bold not ({Show(italic)})");

            var plain = await MenuOn("plain text", "#ag-editor-id p span.ag-plain-text");
            Close(); await Task.Delay(300);
            Check(plain.Values.All(v => v == false), $"in plain text none is pressed ({Show(plain)})");

            // Highlighted text: Highlight pressed, the others not.
            var marked = await MenuOn("highlighted text", "#ag-editor-id mark");
            Close(); await Task.Delay(300);
            Check(marked["ContextFormatHighlight"] == true && marked["ContextFormatStrong"] == false, $"in highlighted text Highlight is pressed, Bold not ({Show(marked)})");

            // Bold pressed in bold text: the bold taken off, and the menu there shows it off.
            await MenuOn("bold again", "#ag-editor-id strong");
            var strong = FindAnywhereInHost(window, "ContextFormatStrong") ?? throw new CaseFailed("no Bold button");
            await ClickInPopup(window, strong);
            var text = "";
            var unbolded = await Eventually(async () => !(text = (string)(await Get(c, id))["text"]!).Contains("**boldwordshere**"), 5000, 200);
            notes.Add($"after Bold: {text.Replace("\n", " | ")}");
            Check(unbolded && text.Contains("boldwordshere"), "Bold in bold text takes the bold off");
            await Task.Delay(500);
            var after = await MenuOn("where the bold was", "#ag-editor-id p span.ag-plain-text");
            Close();
            Check(after["ContextFormatStrong"] == false, $"and the menu there shows Bold off ({Show(after)})");
        }
        finally
        {
            try { Close(); } catch { }
            try { await c.Call("document.save", new { documentId = id }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }

    /// <summary>
    /// XM10 (the user, 2026-11-14): a word with several formats at once - bold and italic, underlined and bold, struck
    /// through and bold. Right-clicked there, the menu shows each of them pressed; the inner one pressed (Italic in
    /// ***x***, Bold in &lt;u&gt;**x**&lt;/u&gt; and ~~**x**~~) takes that format off and leaves the others, and the
    /// one left, pressed in the next menu or by its shortcut (Ctrl+U), takes that off too. Only the outermost format
    /// came off: the inner one pressed changed nothing.
    /// </summary>
    private static async Task XM10(List<string> notes)
    {
        var folder = FixtureFolder("xm10");
        var path = Path.Combine(folder, "xm10.md");
        File.WriteAllText(path, "# Stacked\n\nFirst ***bolditalic*** words.\n\nSecond <u>**boldunder**</u> words.\n\nThird ~~**strikebold**~~ words.\n");
        using var c = await Session("e2e XM10");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        await Task.Delay(1500);
        string[] buttons = { "ContextFormatStrong", "ContextFormatEmphasis", "ContextFormatUnderline", "ContextFormatStrikethrough" };
        Dictionary<string, bool?> States()
        {
            var states = new Dictionary<string, bool?>();
            foreach (var b in buttons)
            {
                var e = FindAnywhereInHost(window, b);
                states[b] = e != null && e.TryGetCurrentPattern(TogglePattern.Pattern, out var p) ? ((TogglePattern)p).Current.ToggleState == ToggleState.On : null;
            }
            return states;
        }
        string Show(Dictionary<string, bool?> s) => string.Join(", ", s.Select(x => $"{x.Key.Replace("ContextFormat", "")}={(x.Value == null ? "none" : x.Value.Value ? "on" : "off")}"));
        void Close() => Send(Key(0x1B, false), Key(0x1B, true));
        // The menu opened by a real right-click on the element; the buttons expected on are on and the others off.
        async Task Menu(string where, string selector, params string[] on)
        {
            await Eventually(() => Task.FromResult(FindAnywhereInHost(window, "SelectAllItem") == null), 3000, 100);
            await ContextMenuAt(c, windowId, window, selector);
            await Task.Delay(400);
            var s = States();
            notes.Add($"{where}: {Show(s)}");
            Check(s.All(x => x.Value == on.Contains(x.Key)), $"{where}: the menu shows {string.Join("+", on.Select(o => o.Replace("ContextFormat", "")))} on, the rest off ({Show(s)})");
        }
        async Task Press(string button)
        {
            var e = FindAnywhereInHost(window, button) ?? throw new CaseFailed($"no {button} button");
            await ClickInPopup(window, e);
        }
        // The paragraph reads `expected` in the document once the format is pressed.
        async Task Expect(string what, string expected)
        {
            var text = "";
            var ok = await Eventually(async () => (text = (string)(await Get(c, id))["text"]!).Contains(expected), 5000, 200);
            notes.Add($"{what}: {text.Replace("\n", " | ")}");
            Check(ok, $"{what}: the paragraph reads \"{expected}\"");
            await Task.Delay(500);
        }
        try
        {
            // Bold and italic: Italic (the inner one) off, then Bold.
            await Menu("in ***bolditalic***", "#ag-editor-id strong em", "ContextFormatStrong", "ContextFormatEmphasis");
            await Press("ContextFormatEmphasis");
            await Expect("Italic pressed in ***bolditalic***", "First **bolditalic** words.");
            await Menu("in **bolditalic**", "#ag-editor-id strong", "ContextFormatStrong");
            await Press("ContextFormatStrong");
            await Expect("then Bold pressed", "First bolditalic words.");

            // Underlined and bold: Bold (the inner one) off, then the underline by its shortcut.
            await Menu("in <u>**boldunder**</u>", "#ag-editor-id u strong", "ContextFormatStrong", "ContextFormatUnderline");
            await Press("ContextFormatStrong");
            await Expect("Bold pressed in <u>**boldunder**</u>", "Second <u>boldunder</u> words.");
            await Menu("in <u>boldunder</u>", "#ag-editor-id u", "ContextFormatUnderline");
            Close(); await Task.Delay(400);
            Chord("Ctrl+U");
            await Expect("then Ctrl+U", "Second boldunder words.");

            // Struck through and bold: Bold (the inner one) off, then Strikethrough.
            await Menu("in ~~**strikebold**~~", "#ag-editor-id del strong", "ContextFormatStrong", "ContextFormatStrikethrough");
            await Press("ContextFormatStrong");
            await Expect("Bold pressed in ~~**strikebold**~~", "Third ~~strikebold~~ words.");
            await Menu("in ~~strikebold~~", "#ag-editor-id del", "ContextFormatStrikethrough");
            await Press("ContextFormatStrikethrough");
            await Expect("then Strikethrough pressed", "Third strikebold words.");
        }
        finally
        {
            try { Close(); } catch { }
            try { await c.Call("document.save", new { documentId = id }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }

    /// <summary>
    /// XM11 (the user, 2026-11-15): a paragraph of one word with several formats, Ctrl+A there (the marks selected
    /// with the word), then a real right-click on it: the menu shows every format of the word pressed, not only the
    /// outermost. The inner one pressed takes it off and leaves the others; the next menu shows that, and the same
    /// button pressed again puts it back (all pressed once more). A format the word lacks goes on once. Plain text
    /// selected with the word shows none pressed.
    /// </summary>
    private static async Task XM11(List<string> notes)
    {
        var folder = FixtureFolder("xm11");
        using var c = await Session("e2e XM11");
        string[] buttons = { "ContextFormatStrong", "ContextFormatEmphasis", "ContextFormatUnderline", "ContextFormatStrikethrough" };
        string Name(string b) => b.Replace("ContextFormat", "");
        string Show(Dictionary<string, bool?> s) => string.Join(", ", s.Select(x => $"{Name(x.Key)}={(x.Value == null ? "none" : x.Value.Value ? "on" : "off")}"));
        void Close() => Send(Key(0x1B, false), Key(0x1B, true));
        var n = 0;
        // A document of the one paragraph, the caret put in its word by a real click and Ctrl+A pressed; then the
        // steps, given the window, the document and the menu check.
        async Task With(string paragraph, string word, Func<IntPtr, string, Func<string, string, string[], Task>, Task> steps)
        {
            var path = Path.Combine(folder, $"xm11-{++n}.md");
            File.WriteAllText(path, paragraph + "\n");
            var id = await Open(c, path);
            var windowId = await WindowIdOf(c, id);
            var window = await WindowOf(windowId, c);
            await Task.Delay(1500);
            Dictionary<string, bool?> States()
            {
                var states = new Dictionary<string, bool?>();
                foreach (var b in buttons)
                {
                    var e = FindAnywhereInHost(window, b);
                    states[b] = e != null && e.TryGetCurrentPattern(TogglePattern.Pattern, out var p) ? ((TogglePattern)p).Current.ToggleState == ToggleState.On : null;
                }
                return states;
            }
            // The menu by a real right-click on the selection: the buttons expected on are on and the others off.
            async Task Menu(string where, string selector, params string[] on)
            {
                await Eventually(() => Task.FromResult(FindAnywhereInHost(window, "SelectAllItem") == null), 3000, 100);
                await ContextMenuAt(c, windowId, window, selector);
                await Task.Delay(400);
                var s = States();
                var selected = (await c.Call("test.editor.eval", new { windowId, script = "getSelection().toString()" }))["result"];
                notes.Add($"{where}: {Show(s)} (selected \"{selected}\")");
                Check(s.All(x => x.Value == on.Contains(x.Key)), $"{where}: the menu shows {(on.Length == 0 ? "none" : string.Join("+", on.Select(Name)))} on, the rest off ({Show(s)})");
            }
            try
            {
                await ClickEditorText(c, windowId, window, word);
                await Task.Delay(400);
                Chord("Ctrl+A");
                await Task.Delay(600);
                await steps(window, id, Menu);
            }
            finally
            {
                try { Close(); } catch { }
                try { await c.Call("document.save", new { documentId = id }); } catch { }
                try { await c.Call("document.close", new { documentId = id }); } catch { }
            }
        }
        async Task Press(IntPtr window, string button)
        {
            var e = FindAnywhereInHost(window, button) ?? throw new CaseFailed($"no {button} button");
            await ClickInPopup(window, e);
        }
        // The document reads one of `expected` once the format is pressed.
        async Task Expect(string id, string what, params string[] expected)
        {
            var text = "";
            var ok = await Eventually(async () => Array.IndexOf(expected, (text = (string)(await Get(c, id))["text"]!).TrimEnd('\n')) >= 0, 5000, 200);
            notes.Add($"{what}: {text.Replace("\n", " | ")}");
            Check(ok, $"{what}: the paragraph reads {string.Join(" or ", expected.Select(e => $"\"{e}\""))}");
            await Task.Delay(500);
        }

        const string B = "ContextFormatStrong", I = "ContextFormatEmphasis", U = "ContextFormatUnderline", S = "ContextFormatStrikethrough";
        // Bold and italic: both shown; Italic (the inner one) off, shown off, and back.
        await With("***bolditalic***", "bolditalic", async (window, id, menu) =>
        {
            await menu("Ctrl+A in ***bolditalic***", "#ag-editor-id strong em", new[] { B, I });
            await Press(window, I);
            await Expect(id, "Italic pressed", "**bolditalic**");
            await menu("then the menu", "#ag-editor-id strong", new[] { B });
            await Press(window, I);
            await Expect(id, "Italic pressed again", "***bolditalic***");
            await menu("and the menu again", "#ag-editor-id strong em", new[] { B, I });
            Close();
        });
        // Underlined and bold: Bold (the inner one) off and back; Italic, which it lacks, goes on once.
        await With("<u>**boldunder**</u>", "boldunder", async (window, id, menu) =>
        {
            await menu("Ctrl+A in <u>**boldunder**</u>", "#ag-editor-id u strong", new[] { B, U });
            await Press(window, B);
            await Expect(id, "Bold pressed", "<u>boldunder</u>");
            await menu("then the menu", "#ag-editor-id u", new[] { U });
            await Press(window, B);
            await Expect(id, "Bold pressed again", "**<u>boldunder</u>**", "<u>**boldunder**</u>");
            await menu("and the menu again", "#ag-editor-id strong", new[] { B, U });
            await Press(window, I);
            await Expect(id, "Italic pressed", "***<u>boldunder</u>***", "*<u>**boldunder**</u>*", "**<u>*boldunder*</u>**", "<u>***boldunder***</u>");
            await menu("and the menu after Italic", "#ag-editor-id em", new[] { B, I, U });
            Close();
        });
        // Struck through and bold: Bold (the inner one) off, then Strikethrough.
        await With("~~**strikebold**~~", "strikebold", async (window, id, menu) =>
        {
            await menu("Ctrl+A in ~~**strikebold**~~", "#ag-editor-id del strong", new[] { B, S });
            await Press(window, B);
            await Expect(id, "Bold pressed", "~~strikebold~~");
            await menu("then the menu", "#ag-editor-id del", new[] { S });
            await Press(window, S);
            await Expect(id, "then Strikethrough pressed", "strikebold");
            Close();
        });
        // Plain text selected with the word: none shown.
        await With("Plain ***bolditalic*** end", "bolditalic", async (window, id, menu) =>
        {
            await menu("Ctrl+A in Plain ***bolditalic*** end", "#ag-editor-id strong em", Array.Empty<string>());
            Close();
        });
    }

    /// <summary>
    /// XM12 (the user, 2026-11-15): a format pressed with the caret alone in plain text, nothing selected, works as in
    /// Word: nothing goes into the text, the format waits for what is typed. Ctrl+B pressed twice at the same caret
    /// leaves the text as it was (no ********), the menu at the caret (Shift+F10) showing Bold pressed in between;
    /// Bold from a real right-click's menu, then a click elsewhere without typing, leaves nothing (no empty **** in the
    /// file); Italic from the Shift+F10 menu twice, the same. Ctrl+B then "abc" typed is **abc**; Ctrl+B, Ctrl+I, "abc"
    /// both; Ctrl+B twice then "abc" plain; one Ctrl+Z after the typing takes the letters out with their marks.
    /// </summary>
    private static async Task XM12(List<string> notes)
    {
        var folder = FixtureFolder("xm12");
        const string original = "# Caret\n\nSome plain words here\n\nOther line\n";
        using var c = await Session("e2e XM12");
        void Close() => Send(Key(0x1B, false), Key(0x1B, true));
        string State(IntPtr window, string button)
        {
            var e = FindAnywhereInHost(window, button);
            return e == null ? "none" : e.TryGetCurrentPattern(TogglePattern.Pattern, out var p) ? ((TogglePattern)p).Current.ToggleState.ToString() : "no toggle";
        }
        var n = 0;
        var wrong = new List<string>();
        // A document of its own for each way; the caret put into "Some plain words here" by a real click.
        // `expected`: the document after the steps, one of them (the original when none is given).
        async Task Way(string what, Func<IntPtr, string, Task> steps, params string[] expected)
        {
            if (expected.Length == 0) expected = new[] { original };
            var path = Path.Combine(folder, $"xm12-{++n}.md");
            File.WriteAllText(path, original);
            var id = await Open(c, path);
            var windowId = await WindowIdOf(c, id);
            var window = await WindowOf(windowId, c);
            await Task.Delay(1500);
            try
            {
                await ClickEditorText(c, windowId, window, "Some plain words here");
                await Task.Delay(400);
                await steps(window, windowId);
                var text = "";
                // The page sends its text a moment after the change; read until it is the one expected or three seconds on.
                await Eventually(async () => Array.IndexOf(expected, text = (string)(await Get(c, id))["text"]!) >= 0, 3000, 200);
                notes.Add($"{what}: {text.Replace("\n", " | ")}");
                // Each way is tried whatever the one before did: all of them are told at the end.
                if (Array.IndexOf(expected, text) < 0) wrong.Add($"{what}: the document reads {Newtonsoft.Json.JsonConvert.ToString(text)}, not {string.Join(" or ", expected.Select(e => Newtonsoft.Json.JsonConvert.ToString(e)))}");
            }
            finally
            {
                try { Close(); } catch { }
                try { await c.Call("document.save", new { documentId = id }); } catch { }
                try { await c.Call("document.close", new { documentId = id }); } catch { }
            }
        }
        async Task MenuPress(IntPtr window, string button, string where)
        {
            var opened = await Eventually(() => Task.FromResult(FindAnywhereInHost(window, button) != null), 3000, 100);
            if (!opened) throw new CaseFailed($"{where}: no menu with {button}");
            await Task.Delay(300);
            notes.Add($"{where}: {button.Replace("ContextFormat", "")} {State(window, button)} before the press");
            await ClickInPopup(window, FindAnywhereInHost(window, button)!);
            await Task.Delay(600);
        }

        // The menu at the caret (Shift+F10 does not move it): the buttons expected pressed are, and closed again.
        async Task MenuShows(IntPtr window, string where, params string[] on)
        {
            Chord("Shift+F10");
            await Eventually(() => Task.FromResult(FindAnywhereInHost(window, "ContextFormatStrong") != null), 3000, 100);
            await Task.Delay(300);
            var states = new[] { "ContextFormatStrong", "ContextFormatEmphasis" }.Select(b => (b, s: State(window, b))).ToList();
            notes.Add($"{where}, the menu at the caret: {string.Join(", ", states.Select(x => $"{x.b.Replace("ContextFormat", "")} {x.s}"))}");
            foreach (var (b, s) in states)
                if ((s == "On") != on.Contains(b)) wrong.Add($"{where}: the menu shows {b.Replace("ContextFormat", "")} {s}");
            Close(); await Task.Delay(400);
        }
        // The caret at the end of "Some plain words here" and a space typed: what is typed next stands as a word.
        async Task AtLineEnd()
        {
            Chord("End"); await Task.Delay(200);
            TypeText(" "); await Task.Delay(500);
        }

        await Way("Ctrl+B twice at the caret", async (window, windowId) =>
        {
            Chord("Ctrl+B");
            await Task.Delay(600);
            await MenuShows(window, "after one Ctrl+B", "ContextFormatStrong");
            Chord("Ctrl+B");
            await Task.Delay(600);
            await MenuShows(window, "after the second Ctrl+B");
        });
        await Way("Ctrl+B, then \"abc\" typed", async (window, windowId) =>
        {
            await AtLineEnd();
            Chord("Ctrl+B"); await Task.Delay(500);
            await MenuShows(window, "after Ctrl+B", "ContextFormatStrong");
            TypeText("abc"); await Task.Delay(800);
        }, "# Caret\n\nSome plain words here **abc**\n\nOther line\n");
        await Way("Ctrl+B, Ctrl+I, then \"abc\" typed", async (window, windowId) =>
        {
            await AtLineEnd();
            Chord("Ctrl+B"); await Task.Delay(400);
            Chord("Ctrl+I"); await Task.Delay(500);
            await MenuShows(window, "after Ctrl+B and Ctrl+I", "ContextFormatStrong", "ContextFormatEmphasis");
            TypeText("abc"); await Task.Delay(800);
        }, "# Caret\n\nSome plain words here ***abc***\n\nOther line\n");
        await Way("Ctrl+B twice, then \"abc\" typed", async (window, windowId) =>
        {
            await AtLineEnd();
            Chord("Ctrl+B"); await Task.Delay(400);
            Chord("Ctrl+B"); await Task.Delay(500);
            TypeText("abc"); await Task.Delay(800);
        }, "# Caret\n\nSome plain words here abc\n\nOther line\n");
        await Way("Ctrl+B, \"abc\" typed, then Ctrl+Z", async (window, windowId) =>
        {
            await AtLineEnd();
            Chord("Ctrl+B"); await Task.Delay(500);
            TypeText("abc"); await Task.Delay(1200);
            Chord("Ctrl+Z"); await Task.Delay(1000);
        }, "# Caret\n\nSome plain words here \n\nOther line\n", "# Caret\n\nSome plain words here\n\nOther line\n");
        await Way("Bold from the right-click menu, then a click on another line", async (window, windowId) =>
        {
            await ContextMenuAt(c, windowId, window, "#ag-editor-id p");
            await MenuPress(window, "ContextFormatStrong", "the right-click menu");
            await ClickEditorText(c, windowId, window, "Other line");
            await Task.Delay(600);
        });
        await Way("Italic from the Shift+F10 menu twice at the caret", async (window, windowId) =>
        {
            Chord("Shift+F10");
            await MenuPress(window, "ContextFormatEmphasis", "the first Shift+F10 menu");
            Chord("Shift+F10");
            await MenuPress(window, "ContextFormatEmphasis", "the second Shift+F10 menu");
        });
        Check(wrong.Count == 0, string.Join("; ", wrong));
    }
}
