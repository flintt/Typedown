using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using Newtonsoft.Json.Linq;

/// <summary>
/// NF01 (the user, 2026-11-23): the notices - every card at the bottom right from one place, and the status bar's bell
/// listing those of this run. An export's card is the card it was (its text, Open, Open file location, close) and its
/// line stays in the list after the card is closed; the first edit's notice, turned off, makes no card but still a line;
/// the bell's dot says there are unread ones, the list shows them newest first with their buttons, opening it reads them,
/// Clear all empties it.
/// </summary>
internal static partial class Program
{
    private static async Task NF01(List<string> notes)
    {
        using var c = await Session("e2e NF01");
        var run = Guid.NewGuid().ToString("N").Substring(0, 8);
        var id = await Open(c, Fixture($"nf01-{run}.md", "# NF01\n\nExported.\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        string? other = null;
        async Task<JObject> State() => (JObject)await c.Call("test.notices.state", new { windowId });
        async Task<JToken?> Setting(string name) => (await c.Call("test.settings.get", new { windowId, name }))["value"];
        var afterExport = (int)(await Setting("AfterExport"))!;
        var warn = (bool?)await Setting("WarnFirstEditRewrite") ?? true;
        var hidden = (string?)await Setting("StatusBarHiddenItems") ?? "";
        try
        {
            await c.Call("window.setView", new { windowId, mode = "visual", bounds = new { x = 40, y = 40, width = 1300, height = 850 } });
            await c.Call("test.settings.set", new { windowId, name = "StatusBarHiddenItems", value = "" });
            await c.Call("test.notices.clear", new { windowId });
            await Task.Delay(500);
            var s = await State();
            Check(((JArray)s["entries"]!).Count == 0 && (int)s["unread"]! == 0, "no notices to start with: " + s.ToString(Newtonsoft.Json.Formatting.None));
            // The dot is a shape, which UI Automation does not see (hp job 717): the bell's name says how many are unread.
            var bell = FindIn(window, "StatusBarNotices");
            Check(bell != null, "the status bar has its bell");
            await Task.Delay(300);
            var readName = FindIn(window, "StatusBarNotices")!.Current.Name;
            notes.Add("the bell, nothing unread: " + readName);

            // An export's card, as it was: its text, Open, Open file location, close.
            await c.Call("test.settings.set", new { windowId, name = "AfterExport", value = 1 });
            var path = Path.Combine(testRoot, $"nf01-{run}.html");
            await c.Call("test.export.run", new { windowId, type = "html", path });
            await Eventually(async () => ((JArray)(await State())["cards"]!).Any(k => (string?)k == "export"), 15000, 200);
            s = await State();
            Check(((JArray)s["cards"]!).Any(k => (string?)k == "export"), "the export's card shows: " + s.ToString(Newtonsoft.Json.Formatting.None));
            var text = FindIn(window, "ExportNoticeText");
            Check(text != null && text.Current.Name.Contains(Path.GetFileName(path)), "its text names the file: " + text?.Current.Name);
            Check(FindIn(window, "ExportNoticeOpen") != null && FindIn(window, "ExportNoticeFolder") != null && FindIn(window, "ExportNoticeClose") != null, "with Open, Open file location and its close button");
            var first = (JObject)((JArray)s["entries"]!)[0];
            Check((string?)first["key"] == "export" && (bool?)first["quiet"] == false && ((JArray)first["actions"]!).Select(a => (string?)a).SequenceEqual(new[] { "ExportNoticeOpen", "ExportNoticeFolder" }),
                "its line in the list, with its two buttons: " + first.ToString(Newtonsoft.Json.Formatting.None));
            Check((int)s["unread"]! == 1, "one unread");
            string BellName() => FindIn(window, "StatusBarNotices")?.Current.Name ?? "";
            await Eventually(async () => { await Task.CompletedTask; return BellName() != readName; }, 3000, 200);
            Check(BellName() != readName && BellName().Contains('1'), "the bell says one is unread: " + BellName());
            notes.Add("shot, the export's card and the bell's dot: " + Screenshot("NF01-1-card"));
            Invoke(FindIn(window, "ExportNoticeClose")!);
            await Eventually(async () => !((JArray)(await State())["cards"]!).Any(k => (string?)k == "export"), 3000, 200);
            s = await State();
            Check(!((JArray)s["cards"]!).Any(k => (string?)k == "export") && ((JArray)s["entries"]!).Count == 1, "closed: the card goes, its line stays");

            // The first edit's notice turned off: no card, its line all the same (only there now).
            await c.Call("test.settings.set", new { windowId, name = "WarnFirstEditRewrite", value = false });
            other = await Open(c, Fixture($"nf01-list-{run}.md", "# NF01 list\n\n8. eight\n10. ten\n"));
            await Eventually(async () => ((JArray)(await State())["entries"]!).Any(e => (string?)e["key"] == "firstEdit"), 15000, 200);
            s = await State();
            var quiet = ((JArray)s["entries"]!).FirstOrDefault(e => (string?)e["key"] == "firstEdit");
            Check(quiet != null && (bool?)quiet["quiet"] == true && ((string?)quiet["where"])?.Contains("nf01-list") == true, "turned off: its line, quiet, about the document: " + quiet?.ToString(Newtonsoft.Json.Formatting.None));
            Check(!((JArray)s["cards"]!).Any(k => (string?)k == "firstEdit") && FindIn(window, "FirstEditNotice") == null, "and no card");
            Check((int)s["unread"]! == 2, "two unread");

            // The bell: the list, newest first, with an entry's buttons; opened, nothing unread.
            Invoke(FindIn(window, "StatusBarNotices")!);
            AutomationElement? newest = null;
            await Eventually(async () => { await Task.CompletedTask; return (newest = FindAnywhereInHost(window, "NoticeEntry0Text")) != null; }, 4000, 200);
            Check(newest != null && newest.Current.Name == (string?)quiet!["text"], "the list's first line is the newest (the first edit's): " + newest?.Current.Name);
            var second = FindAnywhereInHost(window, "NoticeEntry1Text");
            Check(second != null && second.Current.Name.Contains(Path.GetFileName(path)), "then the export's: " + second?.Current.Name);
            Check(FindAnywhereInHost(window, "NoticeEntry1-ExportNoticeFolder") != null, "with its Open file location");
            notes.Add("shot, the list: " + Screenshot("NF01-2-list"));
            await Eventually(async () => (int)(await State())["unread"]! == 0, 3000, 200);
            Check((int)(await State())["unread"]! == 0, "opened: nothing unread");
            await Eventually(async () => { await Task.CompletedTask; return BellName() == readName; }, 3000, 200);
            Check(BellName() == readName, "and the bell no longer says any are: " + BellName());

            // Clear all.
            Invoke(FindAnywhereInHost(window, "NoticesClear")!);
            await Eventually(async () => ((JArray)(await State())["entries"]!).Count == 0, 3000, 200);
            Check(((JArray)(await State())["entries"]!).Count == 0, "Clear all empties the list");
            Check(FindAnywhereInHost(window, "NoticesEmpty") != null, "and says there are none");
            Chord("Esc");
        }
        finally
        {
            try { Chord("Esc"); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "AfterExport", value = afterExport }); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "WarnFirstEditRewrite", value = warn }); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "StatusBarHiddenItems", value = hidden }); } catch { }
            if (other != null) try { await c.Call("document.close", new { documentId = other }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
