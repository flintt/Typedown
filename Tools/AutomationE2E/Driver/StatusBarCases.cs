using System;
using System.Collections.Generic;
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
}
