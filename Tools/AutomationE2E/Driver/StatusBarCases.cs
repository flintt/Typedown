using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using Newtonsoft.Json.Linq;

/// <summary>The status bar's own controls, used as a person uses them: real clicks.</summary>
internal static partial class Program
{
    private static async Task SB01(List<string> notes)
    {
        using var c = await Session("e2e SB01");
        var id = await Open(c, Fixture("sb01.md", "# SB01\n\nSome text.\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        async Task<string?> Mode() => (string?)(await c.Call("window.getView", new { windowId }))["mode"];
        AutomationElement Find(string automationId) =>
            Deep.First(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.AutomationIdProperty, automationId))
            ?? throw new CaseFailed($"no {automationId} in the status bar");
        async Task<string?> ClickAndWait(string automationId)
        {
            await Click(window, Find(automationId));
            await Task.Delay(1000);
            return await Mode();
        }
        try
        {
            await c.Call("window.setView", new { windowId, statusBar = true });
            foreach (var from in new[] { "visual", "source" })
            {
                await c.Call("window.setView", new { windowId, mode = from });
                await Task.Delay(800);
                var reading = await ClickAndWait("StatusBarReading");
                Check(reading == "reading", $"from {from}: the reading button puts the window in reading mode ({reading})");
                var back = await ClickAndWait("StatusBarReadOnlyBadge");
                notes.Add($"from {from}: reading button -> {reading}, badge -> {back}");
                Check(back == from, $"from {from}: the badge goes back to {from} mode ({back})");
            }
            // The mode menu: the current mode ticked, focus and typewriter mode greyed where they do nothing.
            async Task<Dictionary<string, (bool On, bool Enabled)>> Menu()
            {
                await Click(window, Find("StatusBarModes"));
                await Task.Delay(700);
                var root = AutomationElement.FromHandle(window);
                var items = new Dictionary<string, (bool, bool)>();
                foreach (var name in new[] { "StatusSourceCodeItem", "StatusReadOnlyItem", "StatusFocusItem", "StatusTypewriterItem" })
                {
                    var item = Deep.First(root, new PropertyCondition(AutomationElement.AutomationIdProperty, name)) ?? throw new CaseFailed($"no {name} in the mode menu");
                    var on = item.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle) && ((TogglePattern)toggle).Current.ToggleState == ToggleState.On;
                    items[name] = (on, item.Current.IsEnabled);
                }
                return items;
            }
            await c.Call("window.setView", new { windowId, mode = "visual" });
            await Task.Delay(800);
            var visual = await Menu();
            notes.Add("visual: " + string.Join(", ", visual.Select(x => $"{x.Key} {(x.Value.On ? "on" : "off")}{(x.Value.Enabled ? "" : " disabled")}")));
            Check(!visual["StatusSourceCodeItem"].On && !visual["StatusReadOnlyItem"].On, "visual: neither source nor reading mode ticked");
            Check(visual["StatusFocusItem"].Enabled && visual["StatusTypewriterItem"].Enabled, "visual: focus and typewriter mode can be chosen");
            await ClickInPopup(window, Find("StatusSourceCodeItem"));
            await Task.Delay(1000);
            Check(await Mode() == "source", "the mode menu's Source code mode puts the window in source mode");
            var source = await Menu();
            notes.Add("source: " + string.Join(", ", source.Select(x => $"{x.Key} {(x.Value.On ? "on" : "off")}{(x.Value.Enabled ? "" : " disabled")}")));
            Check(source["StatusSourceCodeItem"].On, "source: Source code mode ticked");
            Check(!source["StatusFocusItem"].Enabled && !source["StatusTypewriterItem"].Enabled, "source: focus and typewriter mode greyed");
            Send(Key(0x1B, false), Key(0x1B, true));
            await Task.Delay(400);
        }
        finally
        {
            try { Send(Key(0x1B, false), Key(0x1B, true)); } catch { }
            try { await c.Call("window.setView", new { windowId, mode = "visual" }); } catch { }
        }
    }

    private static async Task SB02(List<string> notes)
    {
        using var c = await Session("e2e SB02");
        // 9 words (Title Hello world see the docs and a cat) and 4 CJK characters, one heading, one link, one picture.
        var id = await Open(c, Fixture("sb02.md", "# Title\n\nHello **world**, see [the docs](https://example.com/a/very/long/address) and ![a cat](cat.png).\n\n汉字かな\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var method = (await c.Call("settings.get", new { keys = new[] { "status.wordCount" } }))["values"]!["status.wordCount"]!;
        AutomationElement? Find(string automationId) =>
            Deep.First(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
        try
        {
            await c.Call("window.setView", new { windowId, statusBar = true, mode = "visual" });
            await Task.Delay(1500);
            var button = Find("StatusBarWordCount") ?? throw new CaseFailed("no word count in the status bar");
            await Click(window, button);
            await Task.Delay(800);
            var values = new Dictionary<string, string>();
            foreach (var key in new[] { "Stats.Words", "Stats.CjkCharacters", "Stats.OtherWords", "Stats.Headings", "Stats.Links", "Stats.Images", "Stats.ReadingTime" })
                values[key] = Find(key)?.Current.Name ?? "(none)";
            notes.Add("popup: " + string.Join(", ", values.Select(v => $"{v.Key}={v.Value}")));
            Check(values["Stats.Words"] == "13", "words: 9 Latin words and 4 CJK characters, the link's address not counted");
            Check(values["Stats.CjkCharacters"] == "4" && values["Stats.OtherWords"] == "9", "CJK characters and other words apart");
            Check(values["Stats.Headings"] == "1" && values["Stats.Links"] == "1" && values["Stats.Images"] == "1", "one heading, one link, one picture");
            Check(values["Stats.ReadingTime"] != "(none)" && values["Stats.ReadingTime"] != "-", "a reading time");
            // What the status bar shows, chosen in the popup.
            var words = Find("ShowWords") ?? throw new CaseFailed("no Words choice in the popup");
            await ClickInPopup(window, words);
            await Task.Delay(800);
            Send(Key(0x1B, false), Key(0x1B, true));
            await Task.Delay(600);
            var shown = Deep.Name(Find("StatusBarWordCount")!);
            var text = Deep.All(Find("StatusBarWordCount")!, Condition.TrueCondition).Cast<AutomationElement>().Select(Deep.Name).FirstOrDefault(n => n.StartsWith("13")) ?? shown;
            notes.Add($"status bar after Words: {text}");
            Check(text.StartsWith("13 "), $"the status bar shows the 13 words once Words is chosen ({text})");
        }
        finally
        {
            try { Send(Key(0x1B, false), Key(0x1B, true)); } catch { }
            try { await SetSetting(c, "status.wordCount", method); } catch { }
        }
    }

    // Auto-save that holds a document back says so in the status bar, with the reason; one that writes shows it saved.
    private static async Task SB03(List<string> notes)
    {
        using var c = await Session("e2e SB03");
        // Bytes that are text in no encoding (0xFF): read with a loss, so auto-save must not write over them.
        var lossy = Path.Combine(fixtures, "sb03-lossy.md");
        var original = Enumerable.Concat(System.Text.Encoding.ASCII.GetBytes("# SB03\n\nSome text "), new byte[] { 0xFF, 0xFF }).Concat(System.Text.Encoding.ASCII.GetBytes(" here.\n")).ToArray();
        File.WriteAllBytes(lossy, original);
        var id = await Open(c, lossy);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var autoSave = (bool)(await c.Call("test.settings.get", new { windowId, name = "AutoSave" }))["value"]!;
        async Task<string> State()
        {
            var e = Deep.First(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.AutomationIdProperty, "StatusBarSaveState"))
                ?? throw new CaseFailed("no save state in the status bar");
            return e.Current.HelpText ?? "";
        }
        try
        {
            await c.Call("window.setView", new { windowId, statusBar = true, mode = "visual" });
            await c.Call("test.settings.set", new { windowId, name = "AutoSave", value = true });
            await ClickEditorText(c, windowId, window, "Some text");
            TypeChar('Q');
            // The save timer ticks every 5 s.
            await Task.Delay(8000);
            var held = await State();
            notes.Add($"lossy file, typed into: '{held.Replace("\n", " | ")}'");
            Check(held.Contains("UTF-8") && held.Contains('\n'), "the status bar says auto-save held the file back, and why");
            Check(File.ReadAllBytes(lossy).SequenceEqual(original), "the file kept its bytes");
            var plain = await Open(c, Fixture("sb03-plain.md", "# SB03 plain\n\nOther text here.\n"));
            await Task.Delay(1500);
            await ClickEditorText(c, windowId, window, "Other text");
            TypeChar('Z');
            await Task.Delay(8000);
            var saved = await State();
            notes.Add($"plain file, typed into: '{saved.Replace("\n", " | ")}'");
            Check(Disk(Path.Combine(fixtures, "sb03-plain.md")).Contains('Z'), "auto-save wrote the plain file");
            Check(!saved.Contains('\n') && !saved.Contains("UTF-8"), "the status bar shows it saved, with no reason left over from the other tab");
        }
        finally
        {
            try { await c.Call("test.settings.set", new { windowId, name = "AutoSave", value = autoSave }); } catch { }
        }
    }

    // The text a status bar button shows (its text blocks, joined).
    private static string BarText(IntPtr window, string automationId)
    {
        var button = Deep.First(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
        if (button == null) return "(none)";
        return string.Join(" ", Deep.All(button, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)).Cast<AutomationElement>().Select(Deep.Name).Where(n => n.Length > 0));
    }

    // A selection shows its count of the whole.
    private static async Task SB04(List<string> notes)
    {
        using var c = await Session("e2e SB04");
        var id = await Open(c, Fixture("sb04.md", "# SB04\n\nalpha beta gamma delta\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var method = (await c.Call("settings.get", new { keys = new[] { "status.wordCount" } }))["values"]!["status.wordCount"]!;
        try
        {
            await c.Call("window.setView", new { windowId, statusBar = true, mode = "visual" });
            await SetSetting(c, "status.wordCount", "words");
            await ClickEditorText(c, windowId, window, "alpha");
            Send(Key(0x24, false), Key(0x24, true));
            await Task.Delay(300);
            var before = BarText(window, "StatusBarWordCount");
            Send(Key(0x10, false), Key(0x23, false), Key(0x23, true), Key(0x10, true));
            await Task.Delay(1200);
            var selected = BarText(window, "StatusBarWordCount");
            notes.Add($"no selection: '{before}'; the line selected: '{selected}'");
            Check(before.StartsWith("5 "), $"5 words without a selection ({before})");
            Check(selected != before && selected.Contains('4') && selected.Contains('5'), $"the 4 selected words of the 5 ({selected})");
            Send(Key(0x24, false), Key(0x24, true));
            await Task.Delay(1200);
            Check(BarText(window, "StatusBarWordCount") == before, "the whole count again once the selection is gone");
        }
        finally
        {
            try { await SetSetting(c, "status.wordCount", method); } catch { }
        }
    }

    // The encoding and line ending: shown, a line ending changed and saved, a GBK file read again as GB18030.
    private static async Task SB05(List<string> notes)
    {
        using var c = await Session("e2e SB05");
        var path = Fixture("sb05.md", "# SB05\n\nline one\nline two\n");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var hidden = (string?)(await c.Call("test.settings.get", new { windowId, name = "StatusBarHiddenItems" }))["value"];
        AutomationElement Find(string automationId) =>
            Deep.First(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.AutomationIdProperty, automationId))
            ?? throw new CaseFailed($"no {automationId}");
        try
        {
            await c.Call("window.setView", new { windowId, statusBar = true, mode = "visual", bounds = new { width = 1400 } });
            await c.Call("test.settings.set", new { windowId, name = "StatusBarHiddenItems", value = "" });
            await Task.Delay(1200);
            var shown = BarText(window, "StatusBarFileFormat");
            Check(shown.Contains("UTF-8") && shown.Contains("LF") && !shown.Contains("CRLF"), $"a new UTF-8 file with LF shows so ({shown})");
            await Click(window, Find("StatusBarFileFormat"));
            await Task.Delay(700);
            await ClickInPopup(window, Find("LineEndingCRLF"));
            await Task.Delay(800);
            var crlf = BarText(window, "StatusBarFileFormat");
            Check(crlf.Contains("CRLF"), $"CRLF chosen ({crlf})");
            var saveState = Find("StatusBarSaveState");
            notes.Add($"save state before the click: '{saveState.Current.HelpText}', at {saveState.Current.BoundingRectangle}");
            await Click(window, saveState);
            // A save flushes the page first: give it time, in a long run too.
            for (var i = 0; i < 30 && !System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path)).Contains("\r\n"); i++) await Task.Delay(200);
            var bytes = File.ReadAllBytes(path);
            notes.Add($"after CRLF and a save: {bytes.Length} bytes, {System.Text.Encoding.ASCII.GetString(bytes).Split("\r\n").Length - 1} CRLF");
            Check(System.Text.Encoding.ASCII.GetString(bytes).Contains("line one\r\nline two\r\n"), "the save wrote CRLF");
            // GBK bytes with no mark: read as UTF-8 they are lost; read again as GB18030 they are the Chinese text.
            var gbk = Path.Combine(fixtures, "sb05-gbk.md");
            File.WriteAllBytes(gbk, new byte[] { 0x23, 0x20, 0xD6, 0xD0, 0xCE, 0xC4, 0xB1, 0xEA, 0xCC, 0xE2, 0x0A, 0x0A, 0xC4, 0xDA, 0xC8, 0xDD, 0x0A });
            var gbkId = await Open(c, gbk);
            await Task.Delay(1500);
            var lossy = BarText(window, "StatusBarFileFormat");
            await Click(window, Find("StatusBarFileFormat"));
            await Task.Delay(700);
            await ClickInPopup(window, Find("ReopenWithEncoding"));
            await Task.Delay(700);
            await ClickInPopup(window, Find("Reopen-gb18030"));
            await Task.Delay(2000);
            var text = (string?)(await Get(c, gbkId))["text"] ?? "";
            var reread = BarText(window, "StatusBarFileFormat");
            notes.Add($"GBK file: shown as '{lossy}', then '{reread}'; text '{text.Replace("\n", "\\n")}'");
            Check(text.Contains("中文标题") && text.Contains("内容"), "read again as GB18030, the text is the Chinese");
            Check(reread.Contains("GB18030"), $"the status bar says GB18030 ({reread})");
            Check(File.ReadAllBytes(gbk).Length == 17, "reading it again wrote nothing");
        }
        finally
        {
            try { Send(Key(0x1B, false), Key(0x1B, true)); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "StatusBarHiddenItems", value = hidden }); } catch { }
        }
    }

    // The heading the caret is under, and a click on it opens the outline.
    private static async Task SB06(List<string> notes)
    {
        using var c = await Session("e2e SB06");
        var id = await Open(c, Fixture("sb06.md", "# First part\n\ntext one\n\n## Second part\n\ntext two\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var view = await c.Call("window.getView", new { windowId });
        try
        {
            await c.Call("window.setView", new { windowId, statusBar = true, mode = "visual", bounds = new { width = 1400 }, sidePane = new { open = false, page = "files" } });
            await ClickEditorText(c, windowId, window, "text two");
            await Task.Delay(1200);
            var heading = BarText(window, "StatusBarHeading");
            notes.Add($"caret in the second part: '{heading}'");
            Check(heading.Contains("Second part"), $"the status bar names the heading the caret is under ({heading})");
            await Click(window, Deep.First(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.AutomationIdProperty, "StatusBarHeading"))!);
            await Task.Delay(1000);
            var after = await c.Call("window.getView", new { windowId });
            Check((bool)after["sidePane"]!["open"]! && (string?)after["sidePane"]!["page"] == "outline", "a click on it opens the outline");
        }
        finally
        {
            try { await c.Call("window.setView", new { windowId, sidePane = new { open = (bool)view["sidePane"]!["open"]!, page = (string)view["sidePane"]!["page"]! } }); } catch { }
        }
    }

    // A narrow window puts what has no room into "...", and a right-click chooses the items.
    private static async Task SB07(List<string> notes)
    {
        using var c = await Session("e2e SB07");
        var id = await Open(c, Fixture("sb07.md", "# A heading with a fairly long name to take room\n\ntext\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var view = await c.Call("window.getView", new { windowId });
        var hidden = (string?)(await c.Call("test.settings.get", new { windowId, name = "StatusBarHiddenItems" }))["value"];
        bool Shown(string automationId) =>
            Deep.First(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.AutomationIdProperty, automationId)) is { } e && !e.Current.IsOffscreen && e.Current.BoundingRectangle.Width > 0;
        try
        {
            await c.Call("window.setView", new { windowId, statusBar = true, mode = "visual", bounds = new { width = 1400 } });
            await c.Call("test.settings.set", new { windowId, name = "StatusBarHiddenItems", value = "" });
            await ClickEditorText(c, windowId, window, "text");
            await Task.Delay(1200);
            var wide = new[] { "StatusBarModes", "StatusBarHeading", "StatusBarFileFormat", "StatusBarSaveState", "StatusBarWordCount", "StatusBarOverflow" }.ToDictionary(x => x, Shown);
            notes.Add("wide: " + string.Join(", ", wide.Select(x => $"{x.Key} {(x.Value ? "shown" : "-")}")));
            Check(wide.Where(x => x.Key != "StatusBarOverflow").All(x => x.Value) && !wide["StatusBarOverflow"], "a wide window shows every item and no \"...\"");
            await c.Call("window.setView", new { windowId, bounds = new { width = 560 } });
            await Task.Delay(1500);
            var narrow = new[] { "StatusBarModes", "StatusBarHeading", "StatusBarFileFormat", "StatusBarSaveState", "StatusBarWordCount", "StatusBarOverflow" }.ToDictionary(x => x, Shown);
            notes.Add("narrow: " + string.Join(", ", narrow.Select(x => $"{x.Key} {(x.Value ? "shown" : "-")}")));
            Check(narrow["StatusBarOverflow"] && !narrow["StatusBarHeading"] && narrow["StatusBarWordCount"], "a narrow window: the heading goes first into \"...\", the count stays");
            await Click(window, Deep.First(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.AutomationIdProperty, "StatusBarOverflow"))!);
            await Task.Delay(700);
            Check(Deep.First(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.AutomationIdProperty, "OverflowHeading")) != null, "the heading is in the \"...\" menu");
            Send(Key(0x1B, false), Key(0x1B, true));
            await Task.Delay(400);
            // Right-click: the count turned off.
            await c.Call("window.setView", new { windowId, bounds = new { width = 1400 } });
            await Task.Delay(1200);
            await Click(window, Deep.First(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.AutomationIdProperty, "StatusBarWordCount"))!, Mouse.Right);
            await Task.Delay(700);
            var countItem = Deep.First(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.AutomationIdProperty, "StatusItem-count")) ?? throw new CaseFailed("no item choice on a right-click");
            await ClickInPopup(window, countItem);
            await Task.Delay(1000);
            Check(!Shown("StatusBarWordCount"), "the count turned off from the right-click menu");
            Check(((string?)(await c.Call("test.settings.get", new { windowId, name = "StatusBarHiddenItems" }))["value"] ?? "").Contains("count"), "and the choice is kept");
        }
        finally
        {
            try { Send(Key(0x1B, false), Key(0x1B, true)); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "StatusBarHiddenItems", value = hidden }); } catch { }
            try { await c.Call("window.setView", new { windowId, bounds = new { width = (int)view["bounds"]!["width"]!, height = (int)view["bounds"]!["height"]! } }); } catch { }
        }
    }
}
