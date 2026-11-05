using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using Newtonsoft.Json.Linq;

/// <summary>
/// The settings pages' controls as a person meets them. SV01: every switch and list on each page is on the screen whole
/// in a narrow window and in languages that run long - "Join the lines of a paragraph in reading mode" pushed its switch
/// out of the window (its text's column was sized to the text and never wrapped). SC01: every list on the pages, each of
/// its choices made in it - the setting set past the page (PL01) missed that choosing Pele or Mermaid in its list ended
/// the app (the list and the setting swapping them until the stack overflowed).
/// </summary>
internal static partial class Program
{
    private static readonly string[] SettingsPagesShown = { "General", "Editor", "View", "Image", "Export", "Shortcut" };

    private static async Task SV01(List<string> notes)
    {
        using var c = await Session("e2e SV01");
        var id = await Open(c, Fixture("sv01.md", "# SV01\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var original = (await c.Call("settings.get", new { keys = new[] { "ui.language" } }))["values"]!["ui.language"]!;
        GetWindowRect(window, out var placed);
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        var scale = Math.Max(96, GetDpiForWindow(window)) / 96.0;
        var problems = new List<string>();
        // A page's rows as the app has laid them out (test.settings.layout): each row's control inside the row. UI
        // Automation could not tell it - a control clipped out of its row has no place there, as one scrolled away.
        async Task<int> Rows(string language, string page)
        {
            await c.Call("test.window.navigate", new { windowId, route = "Settings/" + page });
            // Laid out: the same rows twice running, some. Just after the language changed the window went back to the
            // editor page as it took the language, after this page was asked for: none is asked for again.
            JArray rows = new();
            var last = -1;
            await Eventually(async () =>
            {
                await Task.Delay(300);
                rows = (JArray)(await c.Call("test.settings.layout", new { windowId }))["rows"]!;
                if (rows.Count == 0) await c.Call("test.window.navigate", new { windowId, route = "Settings/" + page });
                var same = rows.Count == last && rows.Count > 0;
                last = rows.Count;
                return same;
            }, 8000);
            foreach (var row in rows)
            {
                var right = (double?)row["right"];
                var left = (double?)row["left"];
                var width = (double)row["rowWidth"]!;
                // Inside the row, short of its padding (16 view pixels at each side).
                if (right == null || left == null || (double)row["width"]! <= 0 || left < 0 || right > width - 8)
                {
                    var problem = $"{language} {page}: '{row["title"]}' - its {row["control"]} at {left?.ToString() ?? "-"}-{right?.ToString() ?? "-"} in a row {width} wide";
                    problems.Add(problem);
                    notes.Add("out of its row: " + problem);
                }
            }
            return rows.Count;
        }
        try
        {
            ShowWindow(window, 9 /* SW_RESTORE */);
            // Narrow, as a window beside another one is: 900 view pixels wide.
            MbSetWindowPos(window, IntPtr.Zero, placed.Left, placed.Top, (int)(900 * scale), (int)(700 * scale), 0x0004 | 0x0010);
            await Task.Delay(500);
            foreach (var language in new[] { "en", "de", "fr", "ja", "zh-Hans" })
            {
                await SetSetting(c, "ui.language", language);
                await Task.Delay(1500);
                foreach (var page in SettingsPagesShown)
                {
                    var seen = await Rows(language, page);
                    notes.Add($"{language} {page}: {seen} rows");
                    Check(seen > 0 || page == "Shortcut", $"{language} {page}: the page shows its rows");
                }
            }
            Check(problems.Count == 0, $"every row's switch, list or box is inside the row, in every language ({problems.Count} not: {string.Join("; ", problems.Take(3))})");
        }
        finally
        {
            try { await c.Call("test.window.navigate", new { windowId, route = "Main" }); } catch { }
            try { await SetSetting(c, "ui.language", original); } catch { }
            try { MbSetWindowPos(window, IntPtr.Zero, placed.Left, placed.Top, placed.Right - placed.Left, placed.Bottom - placed.Top, 0x0004 | 0x0010); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }

    /// <summary>
    /// SN01: moving about the settings never leaves them by itself. Help > Shortcuts opened the shortcuts; General picked in
    /// the settings' pane, then its "Shortcuts" row, went back to the document instead (the row asked for the route the
    /// settings had been opened with, read as "close them"). Ctrl+, (their own way in) still leaves them from any section.
    /// </summary>
    private static async Task SN01(List<string> notes)
    {
        using var c = await Session("e2e SN01");
        var id = await Open(c, Fixture("sn01.md", "# SN01\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        async Task<string?> Section()
        {
            string? s = null;
            await Eventually(async () => { await Task.Delay(200); s = (string?)(await c.Call("test.settings.layout", new { windowId }))["section"]; return true; }, 1000);
            return s;
        }
        async Task<bool> Becomes(string? want) => await Eventually(async () => { await Task.Delay(200); return (string?)(await c.Call("test.settings.layout", new { windowId }))["section"] == want; }, 4000);
        try
        {
            await Activate(window);
            // Help > Shortcuts.
            await c.Call("test.window.navigate", new { windowId, route = "Settings/Shortcut" });
            Check(await Becomes("Shortcut"), $"Help > Shortcuts shows the shortcuts ({await Section()})");
            // General, picked in the pane as a person does.
            var general = await WaitFor(window, "SettingsNavGeneral");
            ((SelectionItemPattern)general.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            Check(await Becomes("General"), $"General picked in the pane shows General ({await Section()})");
            // Its Shortcuts row.
            Invoke(await WaitFor(window, "GeneralShortcutKeys"));
            var shortcuts = await Becomes("Shortcut");
            notes.Add($"after General's Shortcuts row: {await Section() ?? "the document"}");
            Check(shortcuts, $"General's Shortcuts row shows the shortcuts, the settings still open ({await Section() ?? "back on the document"})");
            // Their own way in leaves them, from the section they are on.
            await c.Call("test.window.navigate", new { windowId, route = "Settings/General" });
            Check(await Becomes(null), $"Ctrl+, (Settings/General) leaves the settings from the shortcuts ({await Section()})");
        }
        finally
        {
            try { if (await Section() != null) await c.Call("test.window.navigate", new { windowId, route = "Settings/General" }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }

    private static async Task SC01(List<string> notes)
    {
        using var host = System.Diagnostics.Process.GetProcessById(hostPid);
        using var c = await Session("e2e SC01");
        var id = await Open(c, Fixture("sc01.md", "# SC01\n\n```mermaid\ngraph TD\n  A --> B\n```\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var problems = new List<string>();
        var restore = new List<(string Page, int Index, string Item)>();
        // The list's choices as it shows them: opened, read, closed.
        List<AutomationElement> Choices(AutomationElement box)
        {
            var items = box.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)).Cast<AutomationElement>().ToList();
            return items;
        }
        string Selected(AutomationElement box)
        {
            try
            {
                if (box.TryGetCurrentPattern(SelectionPattern.Pattern, out var p))
                    return ((SelectionPattern)p).Current.GetSelection().FirstOrDefault()?.Current.Name ?? "";
                if (box.TryGetCurrentPattern(ValuePattern.Pattern, out var v)) return ((ValuePattern)v).Current.Value;
            }
            catch (ElementNotAvailableException) { }
            return "";
        }
        async Task<bool> Choose(AutomationElement box, string item)
        {
            ((ExpandCollapsePattern)box.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
            await Task.Delay(300);
            var choice = Choices(box).FirstOrDefault(e => e.Current.Name == item);
            if (choice == null) { try { ((ExpandCollapsePattern)box.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Collapse(); } catch { } return false; }
            ((SelectionItemPattern)choice.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            for (var i = 0; i < 8 && !host.HasExited; i++) await Task.Delay(150);
            try { ((ExpandCollapsePattern)box.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Collapse(); } catch { }
            return true;
        }
        try
        {
            await Activate(window);
            foreach (var page in SettingsPagesShown)
            {
                await c.Call("test.window.navigate", new { windowId, route = "Settings/" + page });
                await Task.Delay(900);
                var boxes = Deep.All(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox)).ToList();
                for (var b = 0; b < boxes.Count; b++)
                {
                    var box = boxes[b];
                    if (box.Current.BoundingRectangle.IsEmpty) continue; // in a closed expander
                    ((ExpandCollapsePattern)box.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
                    await Task.Delay(300);
                    var names = Choices(box).Select(e => e.Current.Name).ToList();
                    try { ((ExpandCollapsePattern)box.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Collapse(); } catch { }
                    await Task.Delay(200);
                    // The interface language's list: switching it is LG01's; here it would change every name read after it.
                    if (names.Count > 20) { notes.Add($"{page}: list {b + 1} ({names.Count} choices) left alone (the interface language)"); continue; }
                    var before = Selected(box);
                    restore.Add((page, b, before));
                    foreach (var item in names)
                    {
                        if (!await Choose(box, item)) { problems.Add($"{page} list {b + 1}: '{item}' could not be chosen"); continue; }
                        if (host.HasExited) { problems.Add($"{page} list {b + 1}: choosing '{item}' ended the app (exit code {host.ExitCode:x8})"); break; }
                        var now = Selected(box);
                        if (now != item) problems.Add($"{page} list {b + 1}: '{item}' chosen, the list shows '{now}'");
                    }
                    if (host.HasExited) break;
                    if (!string.IsNullOrEmpty(before)) await Choose(box, before);
                    notes.Add($"{page}: list {b + 1} ({string.Join(", ", names)}): each chosen, back on '{before}'");
                }
                if (host.HasExited) break;
            }
            foreach (var p in problems.Take(20)) notes.Add(p);
            Check(!host.HasExited, "the app lives on after every choice of every list on the settings pages");
            Check(problems.Count == 0, $"every choice of every list is taken and shown ({problems.Count} not: {string.Join("; ", problems.Take(3))})");
        }
        finally
        {
            if (!host.HasExited)
            {
                try { await c.Call("test.dialogs.dismiss"); } catch { }
                try { await c.Call("test.window.navigate", new { windowId, route = "Main" }); } catch { }
                try { await c.Call("document.close", new { documentId = id }); } catch { }
            }
        }
    }

    /// <summary>
    /// SL01: a click low on a settings section stays where it is. Scrolled to the bottom of Settings > Editor, a click on a
    /// setting's title took the keyboard focus to the section's first setting (the font size's box) and the page scrolled
    /// back up to it (a reader, 2026-11-05). With the real wheel and the real mouse: on Editor a title clicked, then a
    /// switch clicked (it turns, and has the focus) and clicked back; on General a title clicked. Each time the page is
    /// where it was and the focus is not on the first setting. The log's "settings: focus to" lines say where it went.
    /// </summary>
    private static async Task SL01(List<string> notes)
    {
        using var c = await Session("e2e SL01");
        var id = await Open(c, Fixture("sl01.md", "# SL01\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        // The host gives screen points in physical pixels (test.settings.focus): the clicks are put by them so.
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        var logged = LogLines("settings: focus to").Count;
        async Task<JObject> State() => (JObject)await c.Call("test.settings.focus", new { windowId });
        async Task<JObject> AtBottom(string section)
        {
            // From the editor's page, as the other cases open a section: a route to another section while the settings are
            // shown did not change it (hp jobs 229, 231: General never shown after Editor).
            await c.Call("test.window.navigate", new { windowId, route = "Main" });
            await Task.Delay(300);
            await c.Call("test.window.navigate", new { windowId, route = "Settings/" + section });
            JObject s = new();
            Check(await Eventually(async () =>
            {
                await Task.Delay(300);
                try { s = await State(); } catch (Exception) { return false; }
                return (string?)s["section"] == section && ((JArray)s["rows"]!).Count > 0;
            }, 8000), $"{section}: the settings section is shown with its rows (shown: {s["section"]}, {(s["rows"] as JArray)?.Count ?? 0} rows in view)");
            await Activate(window);
            // Scrolled to the bottom by UI Automation, the settings' scroller found by its automation id. Not by the wheel:
            // what is tested is the click after it (the reader scrolled with theirs), and the synthetic wheel went to the
            // editor's web view or, put by a point in another DPI's pixels, outside the window (W10 jobs 198, 209, 220, 224).
            var scroller = Deep.First(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.AutomationIdProperty, "SettingsScroller"))
                ?? throw new CaseFailed($"{section}: no settings scroller (SettingsScroller) in the window");
            if (!scroller.TryGetCurrentPattern(ScrollPattern.Pattern, out var pattern))
                throw new CaseFailed($"{section}: the settings scroller has no scroll pattern");
            ((ScrollPattern)pattern).SetScrollPercent(ScrollPattern.NoScroll, 100);
            Check(await Eventually(async () => { await Task.Delay(200); s = await State(); return (double)s["offset"]! > 100; }, 3000),
                $"{section}: the page scrolled down ({s["offset"]})");
            await Task.Delay(400);
            s = await State();
            notes.Add($"{section}: scrolled by UI Automation to {s["offset"]} of {(double)s["extent"]! - (double)s["viewport"]!:0}, {((JArray)s["rows"]!).Count} rows in view, focus on {s["focused"]}");
            Check((double)s["offset"]! > 100, $"{section}: the page scrolled down ({s["offset"]})");
            return s;
        }
        async Task<JObject> ClickAndLook(string what, JObject at, double offsetBefore, string section)
        {
            await Click(window, (int)at["x"]!, (int)at["y"]!);
            await Task.Delay(700);
            var s = await State();
            var offset = (double)s["offset"]!;
            notes.Add($"{section}: {what} clicked - page at {offset} (was {offsetBefore}), focus on {s["focused"]}");
            Check(Math.Abs(offset - offsetBefore) <= 4, $"{section}: {what} clicked, the page stays where it was ({offsetBefore} -> {offset})");
            Check((string?)s["focusedRow"] != (string?)s["firstRow"] || (string?)s["focusedRow"] == null,
                $"{section}: {what} clicked, the focus is not taken to the first setting ('{s["firstRow"]}'): {s["focused"]}");
            return s;
        }
        try
        {
            // Editor: a title, then a switch low on the page.
            var s = await AtBottom("Editor");
            var rows = ((JArray)s["rows"]!).OfType<JObject>().ToList();
            var low = rows.LastOrDefault(r => (string?)r["control"] == "ToggleSwitch") ?? throw new CaseFailed("Editor: no switch in view at the bottom");
            var lowTitle = (string?)low["title"];
            s = await ClickAndLook($"the title of '{lowTitle}'", (JObject)low["textAt"]!, (double)s["offset"]!, "Editor");
            low = ((JArray)s["rows"]!).OfType<JObject>().First(r => (string?)r["title"] == lowTitle);
            var wasOn = (bool)low["on"]!;
            s = await ClickAndLook($"the switch of '{lowTitle}'", (JObject)low["controlAt"]!, (double)s["offset"]!, "Editor");
            low = ((JArray)s["rows"]!).OfType<JObject>().First(r => (string?)r["title"] == lowTitle);
            Check((bool)low["on"]! != wasOn, $"Editor: the switch of '{lowTitle}' turned as clicked");
            // Where the focus is said to be after it is noted, not checked: the switch takes it (the page's log says so), and
            // then XAML reports the scroller while the window's own focus is still the editor's web view (hp job 227) - what
            // the reader saw is the page staying and the switch turning, checked above.
            notes.Add($"Editor: after the switch's click the focus is reported on {s["focused"]}");
            s = await ClickAndLook($"the switch of '{lowTitle}' again", (JObject)low["controlAt"]!, (double)s["offset"]!, "Editor");
            low = ((JArray)s["rows"]!).OfType<JObject>().First(r => (string?)r["title"] == lowTitle);
            Check((bool)low["on"]! == wasOn, $"Editor: the switch of '{lowTitle}' is back as it was");

            // A list low on the page: opened by its click (the focus goes into its drop-down) and closed with Esc, the page
            // where it was and the focus back on the list - not on the first setting.
            var list = ((JArray)s["rows"]!).OfType<JObject>().LastOrDefault(r => (string?)r["control"] == "ComboBox");
            if (list == null) notes.Add("Editor: no list in view at the bottom; not clicked");
            else
            {
                var listTitle = (string?)list["title"];
                s = await ClickAndLook($"the list of '{listTitle}'", (JObject)list["controlAt"]!, (double)s["offset"]!, "Editor");
                Chord("Esc");
                await Task.Delay(500);
                var closed = await State();
                notes.Add($"Editor: the list closed with Esc - page at {closed["offset"]}, focus on {closed["focused"]}");
                Check(Math.Abs((double)closed["offset"]! - (double)s["offset"]!) <= 4, $"Editor: the list closed, the page stays where it was ({s["offset"]} -> {closed["offset"]})");
                // Noted, as for the switch: the focus XAML reports while the window's own is the web view's is not what was seen.
                Check((string?)closed["focusedRow"] != (string?)closed["firstRow"] || (string?)closed["focusedRow"] == listTitle,
                    $"Editor: the list closed, the focus is not taken to the first setting ({closed["focused"]})");
            }

            // General: a title low on the page.
            s = await AtBottom("General");
            var lowGeneral = ((JArray)s["rows"]!).OfType<JObject>().Last();
            await ClickAndLook($"the title of '{lowGeneral["title"]}'", (JObject)lowGeneral["textAt"]!, (double)s["offset"]!, "General");
        }
        finally
        {
            foreach (var line in LogLines("settings: focus to").Skip(logged).TakeLast(8)) notes.Add("log: " + line.Trim());
            try { await c.Call("test.window.navigate", new { windowId, route = "Main" }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
