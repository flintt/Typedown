using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using Newtonsoft.Json.Linq;
using Typedown.Automation;

internal static partial class Program
{
    // LG02: Chinese menus in the Chinese font after a language switch. Reported (2026-10-27, a Chinese Windows, the app in
    // Chinese): now and then the menus showed Chinese in a thinner font with other glyph shapes - the fallback font of
    // another language - until the app was restarted; the reader had switched to English and back. WinUI picks the
    // fallback font for Chinese by an element's Language, and nothing set it: an element (or the popup window a menu is
    // shown in, which WinUI keeps and shows again) took the language of when it was made.
    // The View menu is opened with the real mouse and its entries' pictures compared with the same entries in a test host
    // started in Chinese, the window's size and place kept (a restart puts it back):
    //   A  started in Chinese, switched to English with View and File opened (their menus made under English), back to
    //      Chinese - the reader's way;
    //   B  the same with no menu opened while in English (expected to match even before the fix);
    //   C  started in English (the W10 PC's Windows is English), View and File opened, switched to Chinese.
    // Where the host has test.window.popupLanguages, the open menu's language tags are noted and must be zh-Hans.
    private static readonly string[] Lg02Entries =
    {
        "SidePaneItem", "SourceCodeModeItem", "FocusModeItem", "TypewriterModeItem", "ReadOnlyModeItem", "StatusBarItem", "FullScreenItem", "ThemeSubMenu", "NextTabItem",
    };

    /// <summary>The most of an entry's pixels that may differ (by more than <see cref="Lg02PixelStep"/> in a channel).</summary>
    private const double Lg02Threshold = 0.01;

    private const int Lg02PixelStep = 48;

    private static async Task LG02(List<string> notes)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        JToken original;
        using (var c0 = await Session("e2e LG02"))
            original = (await c0.Call("settings.get", new { keys = new[] { "ui.language" } }))["values"]!["ui.language"]!;
        var problems = new List<string>();
        var differed = new List<string>();
        var shots = new List<Dictionary<string, Bitmap>>();
        try
        {
            // The reference: a test host started in Chinese.
            var reference = await Lg02Start(notes, "zh-Hans", async (c, windowId, window) => Track(await Lg02View(c, windowId, window, "视图", "lg02-fresh-zh", notes, problems)));

            // A: Chinese -> English (View and File opened) -> Chinese.
            await Lg02Start(notes, "zh-Hans", async (c, windowId, window) =>
            {
                await Lg02Switch(c, windowId, "en", "View");
                Track(await Lg02View(c, windowId, window, "View", "lg02-a-en", notes, null));
                await Lg02OpenAndClose(window, "File");
                await Lg02Switch(c, windowId, "zh-Hans", "视图");
                Lg02Compare("A (zh -> en with View and File opened -> zh)", reference, Track(await Lg02View(c, windowId, window, "视图", "lg02-a-zh", notes, problems)), notes, differed);
                return reference;
            });

            // B: Chinese -> English (no menu opened) -> Chinese.
            await Lg02Start(notes, "zh-Hans", async (c, windowId, window) =>
            {
                await Lg02Switch(c, windowId, "en", "View");
                await Task.Delay(1500);
                await Lg02Switch(c, windowId, "zh-Hans", "视图");
                Lg02Compare("B (zh -> en, no menu opened -> zh)", reference, Track(await Lg02View(c, windowId, window, "视图", "lg02-b-zh", notes, problems)), notes, differed);
                return reference;
            });

            // C: started in English, View and File opened, switched to Chinese.
            await Lg02Start(notes, "en", async (c, windowId, window) =>
            {
                Track(await Lg02View(c, windowId, window, "View", "lg02-c-en", notes, null));
                await Lg02OpenAndClose(window, "File");
                await Lg02Switch(c, windowId, "zh-Hans", "视图");
                Lg02Compare("C (started in en, View and File opened -> zh)", reference, Track(await Lg02View(c, windowId, window, "视图", "lg02-c-zh", notes, problems)), notes, differed);
                return reference;
            });
        }
        finally
        {
            try { await CloseMenus(); } catch { }
            try
            {
                using (var c = await Session("e2e LG02 restore")) await SetSetting(c, "ui.language", original);
                await RestartHost(notes);
            }
            catch (Exception e) { notes.Add("restoring the language: " + e.Message.Split('\n')[0]); }
            foreach (var shot in shots) foreach (var b in shot.Values) b.Dispose();
        }
        notes.Add(differed.Count == 0 ? "every variant matched the host started in Chinese" : "differed from the host started in Chinese: " + string.Join("; ", differed));
        Check(differed.Count == 0, "the Chinese View menu after a language switch looks as in a host started in Chinese: " + string.Join("; ", differed));
        Check(problems.Count == 0, string.Join("; ", problems));

        Dictionary<string, Bitmap> Track(Dictionary<string, Bitmap> shot) { shots.Add(shot); return shot; }
    }

    /// <summary>The test host started again with the interface in a language (set before its window is made), then the body run on its first window.</summary>
    private static async Task<T> Lg02Start<T>(List<string> notes, string language, Func<Client, string, IntPtr, Task<T>> body)
    {
        using (var c = await Session("e2e LG02 language"))
        {
            await SetSetting(c, "ui.language", language);
            await Task.Delay(1500);
        }
        await RestartHost(notes);
        using var client = await Session("e2e LG02 " + language);
        var windowId = await FirstWindowId(client);
        var window = await WindowOf(windowId, client);
        await Task.Delay(2000);
        return await body(client, windowId, window);
    }

    /// <summary>The interface's language changed through the API, waited for until the menu bar says the View menu's title in it.</summary>
    private static async Task Lg02Switch(Client c, string windowId, string language, string viewTitle)
    {
        await SetSetting(c, "ui.language", language);
        var titles = new List<string>();
        for (var i = 0; i < 40 && !titles.Contains(viewTitle); i++)
        {
            await Task.Delay(250);
            try { titles = ((JArray)(await c.Call("test.window.menuTitles", new { windowId }))["titles"]!).Select(t => (string)t!).ToList(); }
            catch (JsonRpcRemoteException) { }
        }
        Check(titles.Contains(viewTitle), $"the menu bar says {viewTitle} after the switch to {language} ({string.Join(" ", titles)})");
        await Task.Delay(1000);
    }

    /// <summary>A menu of the menu bar, by its title, opened with a real click.</summary>
    private static async Task Lg02Open(IntPtr window, string title)
    {
        await Activate(window);
        var root = AutomationElement.FromHandle(window);
        var bar = Deep.First(root, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuBar),
            new PropertyCondition(AutomationElement.FrameworkIdProperty, "XAML"))) ?? throw new CaseFailed("no menu bar in the window");
        var menu = bar.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem))
            .Cast<AutomationElement>().FirstOrDefault(m => m.Current.Name == title) ?? throw new CaseFailed($"no menu {title} in the menu bar");
        await Click(window, menu);
        await Task.Delay(300);
    }

    private static async Task Lg02OpenAndClose(IntPtr window, string title)
    {
        await Lg02Open(window, title);
        await Task.Delay(1000);
        await CloseMenus();
        await Task.Delay(500);
    }

    /// <summary>
    /// The View menu opened with the mouse and each of its entries captured from the screen (the whole menu saved as a
    /// picture beside the results), with the menu's language tags and an entry's UI Automation culture in the notes.
    /// </summary>
    private static async Task<Dictionary<string, Bitmap>> Lg02View(Client c, string windowId, IntPtr window, string title, string name, List<string> notes, List<string>? problems)
    {
        await Lg02Open(window, title);
        AutomationElement? first = null;
        if (!await Eventually(() => Task.FromResult((first = FindAnywhereInHost(window, Lg02Entries[0])) != null && !first.Current.IsOffscreen), 4000, 100))
            throw new CaseFailed($"the View menu ({title}) did not open");
        // Its opening animation over.
        await Task.Delay(900);
        var shot = new Dictionary<string, Bitmap>();
        var all = System.Windows.Rect.Empty;
        foreach (var id in Lg02Entries)
        {
            var entry = FindAnywhereInHost(window, id);
            if (entry == null || entry.Current.IsOffscreen) continue;
            var r = entry.Current.BoundingRectangle;
            if (r.IsEmpty || r.Width < 4 || r.Height < 4) continue;
            all.Union(r);
            var b = new Bitmap((int)r.Width, (int)r.Height);
            using (var g = Graphics.FromImage(b)) g.CopyFromScreen((int)r.Left, (int)r.Top, 0, 0, new Size((int)r.Width, (int)r.Height));
            shot[id] = b;
        }
        if (!all.IsEmpty)
        {
            using var whole = new Bitmap((int)all.Width, (int)all.Height);
            using (var g = Graphics.FromImage(whole)) g.CopyFromScreen((int)all.Left, (int)all.Top, 0, 0, new Size((int)all.Width, (int)all.Height));
            var file = Path.Combine(outputDir, name + ".png");
            whole.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            notes.Add($"{name}: {shot.Count} entries, {file}");
        }
        try
        {
            var culture = first!.GetCurrentPropertyValue(AutomationElement.CultureProperty);
            notes.Add($"{name}: UI Automation culture of {first.Current.Name}: {culture}");
        }
        catch (Exception e) { notes.Add($"{name}: no UI Automation culture ({e.GetType().Name})"); }
        try
        {
            var tags = await c.Call("test.window.popupLanguages", new { windowId });
            var popups = (JArray)tags["popups"]!;
            notes.Add($"{name}: interface {tags["interface"]}, root {tags["root"]}; popups: " + string.Join(" / ", popups.Select(p =>
                $"{p["type"]} popup={p["popup"]} child={p["child"]} texts={string.Join(",", ((JArray)p["texts"]!).Select(t => (string?)t["language"]).Distinct())}")));
            if (problems != null)
                foreach (var p in popups.Where(p => (string?)p["type"] == "MenuFlyoutPresenter"))
                    foreach (var t in ((JArray)p["texts"]!).Where(t => System.Text.RegularExpressions.Regex.IsMatch((string?)t["text"] ?? "", @"[㐀-鿿]")))
                        if (!string.Equals((string?)t["language"], "zh-Hans", StringComparison.OrdinalIgnoreCase))
                        {
                            problems.Add($"{name}: '{t["text"]}' carries the language {t["language"]}, not zh-Hans");
                            break;
                        }
        }
        catch (JsonRpcRemoteException e) { notes.Add($"{name}: no test.window.popupLanguages in this host ({e.Message.Split('\n')[0]})"); }
        await CloseMenus();
        await Task.Delay(500);
        return shot;
    }

    /// <summary>Each entry's picture against the reference's: the share of pixels that differ, the worst one named.</summary>
    private static void Lg02Compare(string variant, Dictionary<string, Bitmap> reference, Dictionary<string, Bitmap> shot, List<string> notes, List<string> differed)
    {
        var results = new List<(string Id, double Share)>();
        foreach (var (id, a) in reference)
        {
            if (!shot.TryGetValue(id, out var b)) continue;
            int w = Math.Min(a.Width, b.Width), h = Math.Min(a.Height, b.Height), off = 0;
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                    if (Math.Max(Math.Abs(p.R - q.R), Math.Max(Math.Abs(p.G - q.G), Math.Abs(p.B - q.B))) > Lg02PixelStep) off++;
                }
            var share = (double)off / Math.Max(1, w * h);
            // Of another width: the menu was laid out with other text widths, which is the fault itself.
            if (a.Width != b.Width) share = Math.Max(share, Lg02Threshold * 2);
            results.Add((id, share));
        }
        var worst = results.OrderByDescending(r => r.Share).FirstOrDefault();
        notes.Add($"{variant}: {results.Count} entries compared, worst {worst.Id} {worst.Share:P2} of its pixels differ; " + string.Join(", ", results.Select(r => $"{r.Id} {r.Share:P1}")));
        if (results.Count < 5) differed.Add($"{variant}: only {results.Count} entries to compare");
        else if (worst.Share > Lg02Threshold) differed.Add($"{variant}: {worst.Id} {worst.Share:P2}");
    }
}
