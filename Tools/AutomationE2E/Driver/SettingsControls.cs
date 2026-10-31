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
}
