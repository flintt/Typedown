using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UIA = System.Windows.Automation;

/// <summary>
/// BL01: blank lines are kept through a visual edit and a save. Extra blank lines show as empty paragraphs, fewer of
/// them than there are lines, and an even run lost a line as soon as the reader typed anything: 2 came back as 1, 4 as
/// 3 (todo S8, a review from 2022). The page side is checked headless (Tools/EditorBench/blanklines-check.js); this is
/// the same through the app: the keystroke, the text the host holds, and the file a save writes.
/// BL02: Settings > Editor > "Show extra blank lines" (off by default): with it on, the empty lines between paragraphs
/// keep their height in reading mode and in an HTML export too, as while editing; off, both collapse them as before.
/// The toggle is found and switched on the settings page, as a person does it.
/// </summary>
internal static partial class Program
{
    private static async Task BL01(List<string> notes)
    {
        using var c = await Session("e2e BL01");
        const string source = "\n# BL01\n\n\nTwo blank lines above.\n\n\n\n\nFour above.\n\n\n- a list\n\n\n\n> a quote\n\n\n\n\n\n| a | b |\n| --- | --- |\n| 1 | 2 |\n\n\nLast\n\n";
        var path = Fixture("bl01.md", source);
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        try
        {
            await WaitUntilDrawn(c, windowId, id, "Four above.");
            // Ctrl+End and a key: the caret goes to the end of "Last" (one blank line after it is no empty paragraph).
            await TypeIntoWindow(c, windowId, "X");
            var expected = source.Replace("Last\n", "LastX\n");
            string text = "";
            for (var i = 0; i < 50; i++)
            {
                text = (string?)(await Get(c, id))["text"] ?? "";
                if (text != source) break;
                await Task.Delay(200);
            }
            notes.Add("after the key: " + text.Replace("\n", "\\n"));
            Check(text != source, "the key reached the document");
            // A visual edit writes the table back in its own style (padded to the column width with "Align table columns",
            // on by default; compact without): its cells' spacing is the table's, not a blank line, so it is compared
            // with runs of spaces in table rows taken as one. Every line break is compared exactly.
            static string Cells(string t) => System.Text.RegularExpressions.Regex.Replace(t, @"(?m)^\|.*$", m => System.Text.RegularExpressions.Regex.Replace(m.Value, " +", " "));
            Check(Cells(text) == Cells(expected), "the edit added the key and nothing else: one blank line at the start, then 2, 4, 2, 3, 5 and 2 between the blocks and one at the end, as in the file");
            await c.Call("document.save", new { documentId = id });
            var saved = Disk(path);
            Check(saved == text, "the file a save writes is that text");
        }
        finally
        {
            try { await c.Call("document.close", new { documentId = id }); } catch { }
            try { File.Delete(path); } catch { }
        }
    }

    private static async Task BL02(List<string> notes)
    {
        using var c = await Session("e2e BL02");
        const string source = "# BL02\n\nFirst\n\n\n\n\n\nSecond\n"; // five blank lines: two empty paragraphs
        var path = Fixture("bl02.md", source);
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var before = (bool)(await c.Call("test.settings.get", new { windowId, name = "ShowBlankLines" }))["value"]!;
        var readOnly = (bool)(await c.Call("test.settings.get", new { windowId, name = "ReadOnly" }))["value"]!;
        const string gapScript = "(() => { const ps = [...document.querySelectorAll('#ag-editor-id > p')]; const a = ps.find(p => p.textContent.includes('First')); const b = ps.find(p => p.textContent.includes('Second')); return a && b ? Math.round(b.getBoundingClientRect().top - a.getBoundingClientRect().bottom) : -1 })()";
        async Task<double> Gap() => (double?)(await c.Call("test.editor.eval", new { windowId, script = gapScript }))["result"] ?? -1;
        async Task<string> Export(string name)
        {
            var file = Path.Combine(testRoot, name);
            if (File.Exists(file)) File.Delete(file);
            await c.Call("test.export.run", new { windowId, type = "html", path = file });
            for (var i = 0; i < 75 && !File.Exists(file); i++) await Task.Delay(200);
            await Task.Delay(500);
            return File.Exists(file) ? File.ReadAllText(file) : throw new CaseFailed($"the export {name} was not written");
        }
        async Task Toggle(bool on)
        {
            await c.Call("test.window.navigate", new { windowId, route = "Settings" });
            await Task.Delay(1500);
            await Activate(window);
            var root = UIA.AutomationElement.FromHandle(window);
            var nav = Deep.First(root, new UIA.PropertyCondition(UIA.AutomationElement.AutomationIdProperty, "SettingsNavEditor")) ?? throw new CaseFailed("no Editor item on the settings page");
            if (nav.TryGetCurrentPattern(UIA.SelectionItemPattern.Pattern, out var select)) ((UIA.SelectionItemPattern)select).Select();
            else ((UIA.InvokePattern)nav.GetCurrentPattern(UIA.InvokePattern.Pattern)).Invoke();
            await Task.Delay(1500);
            var toggle = Deep.First(root, new UIA.PropertyCondition(UIA.AutomationElement.AutomationIdProperty, "ShowBlankLinesToggle")) ?? throw new CaseFailed("no \"Show extra blank lines\" switch on the Editor settings");
            var pattern = (UIA.TogglePattern)toggle.GetCurrentPattern(UIA.TogglePattern.Pattern);
            if ((pattern.Current.ToggleState == UIA.ToggleState.On) != on) pattern.Toggle();
            await Task.Delay(300);
            Check((bool)(await c.Call("test.settings.get", new { windowId, name = "ShowBlankLines" }))["value"]! == on, $"the switch turns the setting {(on ? "on" : "off")}");
            await c.Call("test.window.navigate", new { windowId, route = "Main" });
            await WaitUntilDrawn(c, windowId, id, "Second");
        }
        try
        {
            await c.Call("test.settings.set", new { windowId, name = "ShowBlankLines", value = false });
            await WaitUntilDrawn(c, windowId, id, "Second");
            var editing = await Gap();
            await c.Call("test.settings.set", new { windowId, name = "ReadOnly", value = true });
            await Task.Delay(800);
            var readingOff = await Gap();
            var htmlOff = await Export("bl02-off.html");
            await c.Call("test.settings.set", new { windowId, name = "ReadOnly", value = false });

            await Toggle(true);
            await c.Call("test.settings.set", new { windowId, name = "ReadOnly", value = true });
            await Task.Delay(800);
            var readingOn = await Gap();
            var htmlOn = await Export("bl02-on.html");
            notes.Add($"gap while editing {editing}px; reading off {readingOff}px, on {readingOn}px");
            Check(editing > 40, $"while editing the two empty lines take room ({editing}px)");
            Check(readingOff < editing / 2, $"off (the default), reading mode collapses them as before ({readingOff}px)");
            Check(Math.Abs(readingOn - editing) <= 3, $"on, reading mode keeps them as while editing ({readingOn}px)");
            Check(!htmlOff.Contains("<p><br></p>"), "off, the HTML export collapses them as before");
            Check(htmlOn.Split("<p><br></p>").Length - 1 == 2, "on, the HTML export writes them as two empty lines");
            Check((string?)(await Get(c, id))["text"] == source, "the document text is the same either way");
        }
        finally
        {
            try { await c.Call("test.window.navigate", new { windowId, route = "Main" }); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "ReadOnly", value = readOnly }); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "ShowBlankLines", value = before }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
            try { File.Delete(path); } catch { }
        }
    }
}
