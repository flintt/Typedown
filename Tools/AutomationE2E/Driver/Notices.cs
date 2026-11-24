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
        // The first edit's notice turned off before the document is opened, the document the case's first (as F03 has
        // it): one opened second, in a window of many documents, was never drawn and told nothing (w10 jobs 716, 719).
        var first = await FirstWindowId(c);
        async Task<JToken?> SettingOf(string w, string name) => (await c.Call("test.settings.get", new { windowId = w, name }))["value"];
        var warn = (bool?)await SettingOf(first, "WarnFirstEditRewrite") ?? true;
        var afterExport = (int)(await SettingOf(first, "AfterExport"))!;
        var hidden = (string?)await SettingOf(first, "StatusBarHiddenItems") ?? "";
        await c.Call("test.settings.set", new { windowId = first, name = "WarnFirstEditRewrite", value = false });
        await c.Call("test.settings.set", new { windowId = first, name = "StatusBarHiddenItems", value = "" });
        await c.Call("test.notices.clear", new { windowId = first });
        string? id = null;
        try
        {
            id = await Open(c, Fixture($"nf01-{run}.md", UnsafeSample));
            var windowId = await WindowIdOf(c, id);
            var window = await WindowOf(windowId, c);
            async Task<JObject> State() => (JObject)await c.Call("test.notices.state", new { windowId });
            // The dot is a shape, which UI Automation does not see (hp job 717): the bell's name says how many are unread.
            string BellName() => FindIn(window, "StatusBarNotices")?.Current.Name ?? "";
            await c.Call("window.setView", new { windowId, mode = "visual", bounds = new { x = 40, y = 40, width = 1300, height = 850 } });
            Check(FindIn(window, "StatusBarNotices") != null, "the status bar has its bell");

            // The first edit's notice turned off: no card, its line all the same (only there now).
            await Eventually(async () => ((JArray)(await State())["entries"]!).Any(e => (string?)e["key"] == "firstEdit"), 15000, 200);
            var s = await State();
            var quiet = ((JArray)s["entries"]!).FirstOrDefault(e => (string?)e["key"] == "firstEdit");
            Check(quiet != null && (bool?)quiet["quiet"] == true && ((string?)quiet["where"])?.Contains($"nf01-{run}") == true, "turned off: its line, quiet, about the document: " + s.ToString(Newtonsoft.Json.Formatting.None));
            Check(!((JArray)s["cards"]!).Any(k => (string?)k == "firstEdit") && FindIn(window, "FirstEditNotice") == null, "and no card");
            Check((int)s["unread"]! == 1, "one unread");
            await Eventually(async () => { await Task.CompletedTask; return BellName().Contains('1'); }, 3000, 200);
            Check(BellName().Contains('1'), "the bell says one is unread: " + BellName());

            // An export's card, as it was: its text, Open, Open file location, close; its line kept once it is closed.
            await c.Call("test.settings.set", new { windowId, name = "AfterExport", value = 1 });
            var path = Path.Combine(testRoot, $"nf01-{run}.html");
            await c.Call("test.export.run", new { windowId, type = "html", path });
            await Eventually(async () => ((JArray)(await State())["cards"]!).Any(k => (string?)k == "export"), 15000, 200);
            s = await State();
            Check(((JArray)s["cards"]!).Any(k => (string?)k == "export"), "the export's card shows: " + s.ToString(Newtonsoft.Json.Formatting.None));
            var text = FindIn(window, "ExportNoticeText");
            Check(text != null && text.Current.Name.Contains(Path.GetFileName(path)), "its text names the file: " + text?.Current.Name);
            Check(FindIn(window, "ExportNoticeOpen") != null && FindIn(window, "ExportNoticeFolder") != null && FindIn(window, "ExportNoticeClose") != null, "with Open, Open file location and its close button");
            var newest = (JObject)((JArray)s["entries"]!)[0];
            Check((string?)newest["key"] == "export" && (bool?)newest["quiet"] == false && ((JArray)newest["actions"]!).Select(a => (string?)a).SequenceEqual(new[] { "ExportNoticeOpen", "ExportNoticeFolder" }),
                "its line in the list, newest, with its two buttons: " + newest.ToString(Newtonsoft.Json.Formatting.None));
            Check((int)s["unread"]! == 2, "two unread");
            await Eventually(async () => { await Task.CompletedTask; return BellName().Contains('2'); }, 3000, 200);
            var unreadName = BellName();
            Check(unreadName.Contains('2'), "the bell says two are unread: " + unreadName);
            notes.Add("shot, the export's card and the bell's dot: " + Screenshot("NF01-1-card"));

            // Under a custom theme the card is in its colours (the user, 2026-11-24: the notices went by light and dark
            // only): Dracula's panels, #343746, in the card's padding.
            var themeBefore = (string?)(await c.Call("test.settings.get", new { windowId, name = "CustomTheme" }))["value"] ?? "";
            await c.Call("test.settings.set", new { windowId, name = "CustomTheme", value = "dracula" });
            await Task.Delay(1200);
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            // (The card, a Border, is not in UI Automation's tree - job 736: its text is, 16 in from its left.)
            var card = FindIn(window, "ExportNoticeText")!.Current.BoundingRectangle;
            System.Drawing.Color Pixel(int x, int y)
            {
                using var shot = new System.Drawing.Bitmap(1, 1);
                using (var g = System.Drawing.Graphics.FromImage(shot)) g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(1, 1));
                return shot.GetPixel(0, 0);
            }
            var inPadding = Pixel((int)card.Left - 8, (int)(card.Top + card.Height / 2));
            notes.Add($"under Dracula the card's padding is #{inPadding.R:X2}{inPadding.G:X2}{inPadding.B:X2} {Screenshot("NF01-1b-dracula")}");
            Check(Math.Abs(inPadding.R - 0x34) <= 8 && Math.Abs(inPadding.G - 0x37) <= 8 && Math.Abs(inPadding.B - 0x46) <= 8, $"under Dracula the card is in its panels' colour (#343746): #{inPadding.R:X2}{inPadding.G:X2}{inPadding.B:X2}");
            Invoke(FindIn(window, "StatusBarNotices")!);
            await Task.Delay(900);
            notes.Add("shot, the list under Dracula: " + Screenshot("NF01-1c-dracula-list"));
            Chord("Esc");
            await Task.Delay(300);
            await c.Call("test.settings.set", new { windowId, name = "CustomTheme", value = themeBefore });
            await Task.Delay(800);
            // (The list was opened: read; the rest of the case counts from here.)
            await Eventually(async () => (int)(await State())["unread"]! == 0, 3000, 200);
            // (Ten seconds after it came, it went by itself: the theme's part above takes a few.)
            if (FindIn(window, "ExportNoticeClose") is { } closeButton) Invoke(closeButton);
            else notes.Add("the export's card had gone by itself before its close button was pressed");
            await Eventually(async () => !((JArray)(await State())["cards"]!).Any(k => (string?)k == "export"), 3000, 200);
            s = await State();
            Check(!((JArray)s["cards"]!).Any(k => (string?)k == "export") && ((JArray)s["entries"]!).Count == 2, "closed: the card goes, its line stays");

            // The bell: the list, newest first, with an entry's buttons; opened, nothing unread.
            Invoke(FindIn(window, "StatusBarNotices")!);
            AutomationElement? line0 = null;
            await Eventually(async () => { await Task.CompletedTask; return (line0 = FindAnywhereInHost(window, "NoticeEntry0Text")) != null; }, 4000, 200);
            Check(line0 != null && line0.Current.Name.Contains(Path.GetFileName(path)), "the list's first line is the newest (the export's): " + line0?.Current.Name);
            Check(FindAnywhereInHost(window, "NoticeEntry0-ExportNoticeFolder") != null, "with its Open file location");
            var line1 = FindAnywhereInHost(window, "NoticeEntry1Text");
            Check(line1 != null && line1.Current.Name == (string?)quiet!["text"], "then the first edit's: " + line1?.Current.Name);
            // A moment for the popup to be drawn: found at once by UI Automation, the shot had only the bell (job 723).
            await Task.Delay(700);
            notes.Add("shot, the list: " + Screenshot("NF01-2-list"));
            await Eventually(async () => (int)(await State())["unread"]! == 0, 3000, 200);
            Check((int)(await State())["unread"]! == 0, "opened: nothing unread");
            await Eventually(async () => { await Task.CompletedTask; return BellName() != unreadName; }, 3000, 200);
            Check(BellName() != unreadName && !BellName().Contains('2'), "and the bell no longer says any are: " + BellName());

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
            try { await c.Call("test.settings.set", new { windowId = first, name = "AfterExport", value = afterExport }); } catch { }
            try { await c.Call("test.settings.set", new { windowId = first, name = "WarnFirstEditRewrite", value = warn }); } catch { }
            try { await c.Call("test.settings.set", new { windowId = first, name = "StatusBarHiddenItems", value = hidden }); } catch { }
            if (id != null) try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
