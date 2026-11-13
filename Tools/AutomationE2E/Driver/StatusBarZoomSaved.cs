using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Automation;

/// <summary>
/// SB09 (the user, 2026-11-13): the status bar says when the file was last saved and has the editor's zoom.
/// - a file last written long ago: its date ("Last saved 3/4/2025", as the home page dates a file older than a week), the
///   time in the tip (its help text);
/// - typed in and saved from the bar: the time now (no longer that year);
/// - the zoom's percent 100%; its + a step up (110%, the font size with it), its - back, the percent back to 100%;
/// - both among the items a right-click chooses.
/// </summary>
internal static partial class Program
{
    private static async Task SB09(List<string> notes)
    {
        using var c = await Session("e2e SB09");
        var path = Fixture("sb09.md", "# SB09\n\nSome text.\n");
        var old = new DateTime(2025, 3, 4, 10, 7, 0);
        File.SetLastWriteTime(path, old);
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        AutomationElement? Find(string automationId) =>
            Deep.First(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
        string LastSaved() => Find("StatusBarLastSaved") is { } e && !e.Current.IsOffscreen ? Deep.Name(e) : "";
        async Task<double> FontSize() => (double)(await c.Call("test.settings.get", new { windowId, name = "FontSize" }))["value"]!;
        var fontSize = await FontSize();
        try
        {
            await c.Call("window.setView", new { windowId, statusBar = true, bounds = new { x = 40, y = 40, width = 1400, height = 800 } });
            await c.Call("test.settings.set", new { windowId, name = "StatusBarHiddenItems", value = "" });

            // Last saved: the file's time on disk.
            var shown = "";
            await Eventually(() => Task.FromResult((shown = LastSaved()).Length > 0), 5000, 200);
            var tip = Find("StatusBarLastSaved")?.Current.HelpText ?? "";
            notes.Add($"opened (written {old:yyyy-MM-dd HH:mm}): \"{shown}\", tip \"{tip}\"");
            Check(shown.Contains("2025") && tip.Contains("2025") && tip.Contains("10:07"), "a file last written long ago: its date, the time in the tip");

            // Typed in and saved from the bar: the time now.
            await TypeInto(c, id, " more");
            await Eventually(async () => !(bool)(await Get(c, id))["saved"]!, 5000, 200);
            await Click(window, Find("StatusBarSaveState") ?? throw new CaseFailed("no save state"));
            var saved = await Eventually(() => Task.FromResult(!(shown = LastSaved()).Contains("2025") && shown.Length > 0), 8000, 200);
            notes.Add($"saved from the bar: \"{shown}\" (now {DateTime.Now:HH:mm})");
            Check(saved && shown.Contains(DateTime.Now.ToString("mm")) , "saved: the time now");

            // Zoom.
            await c.Call("test.settings.set", new { windowId, name = "FontSize", value = 16.0 });
            await Task.Delay(500);
            string Percent() => BarText(window, "StatusBarZoomReset");
            notes.Add($"zoom: {Percent()}");
            Check(Percent() == "100%", "the zoom at the default font size: 100%");
            await Click(window, Find("StatusBarZoomIn") ?? throw new CaseFailed("no zoom in"));
            await Task.Delay(600);
            var up = Percent();
            var upSize = await FontSize();
            notes.Add($"+: {up}, font size {upSize}");
            Check(up == "110%" && Math.Abs(upSize - 17.6) < 0.01, "+ zooms in a step (110%, the font size with it)");
            await Click(window, Find("StatusBarZoomOut") ?? throw new CaseFailed("no zoom out"));
            await Task.Delay(600);
            Check(Percent() == "100%", $"- zooms back ({Percent()})");
            await Click(window, Find("StatusBarZoomIn")!);
            await Task.Delay(300);
            await Click(window, Find("StatusBarZoomIn")!);
            await Task.Delay(600);
            var twice = Percent();
            await Click(window, Find("StatusBarZoomReset")!);
            await Task.Delay(600);
            notes.Add($"twice: {twice}, then the percent clicked: {Percent()}, font size {await FontSize()}");
            Check(twice == "121%" && Percent() == "100%" && Math.Abs(await FontSize() - 16) < 0.01, "a click on the percent: back to 100%");

            // Both among the items a right-click chooses.
            await Click(window, Find("StatusBarWordCount") ?? throw new CaseFailed("no count"), Mouse.Right);
            await Task.Delay(700);
            Check(Find("StatusItem-lastsaved") != null && Find("StatusItem-zoom") != null, "the right-click lists last saved and zoom");
        }
        finally
        {
            try { Send(Key(0x1B, false), Key(0x1B, true)); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "FontSize", value = fontSize }); } catch { }
            try { await c.Call("document.save", new { documentId = id }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
