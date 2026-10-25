using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UIA = System.Windows.Automation;

/// <summary>
/// FT01: the order of the file tree (names compared with the current culture). .NET Core 3.1 compared with Windows' NLS,
/// .NET 10 with ICU unless told otherwise; names with '-', '_', spaces, digits, accents and Chinese show a difference.
/// The case records the order; the expected one is the order 1.3.7 shows.
/// </summary>
internal static partial class Program
{
    private static readonly string[] SortNames = { "a-b.md", "a_b.md", "ab.md", "a b.md", "A1.md", "a10.md", "a2.md", "éclair.md", "eclair.md", "中文.md", "zeta.md", "Zebra.md", "'quote.md", "-dash.md", "_under.md", "1two.md" };

    // The order Typedown 1.3.7 (.NET Core 3.1, NLS) shows these names in, measured with FT01 on it.
    private static readonly string[] SortExpected = { "_under.md", "1two.md", "a b.md", "a_b.md", "A1.md", "a10.md", "a2.md", "ab.md", "a-b.md", "-dash.md", "eclair.md", "éclair.md", "'quote.md", "Zebra.md", "zeta.md", "中文.md" };

    private static async Task FT01(List<string> notes)
    {
        using var c = await Session("e2e FT01");
        var dir = Path.Combine(fixtures, "ft01");
        Directory.CreateDirectory(dir);
        foreach (var n in SortNames) File.WriteAllText(Path.Combine(dir, n), "# " + n + "\n");
        var first = (string)((Newtonsoft.Json.Linq.JArray)(await c.Call("window.list"))["windows"]!)[0]!["windowId"]!;
        var action = (await c.Call("test.settings.get", new { windowId = first, name = "FolderStartupAction" }))["value"];
        var folder = (await c.Call("test.settings.get", new { windowId = first, name = "StartupOpenFolder" }))["value"];
        // A window opened now loads this folder as it starts (Settings > General > startup folder).
        await c.Call("test.settings.set", new { windowId = first, name = "StartupOpenFolder", value = dir });
        await c.Call("test.settings.set", new { windowId = first, name = "FolderStartupAction", value = 2 /* OpenFolder */ });
        var windowId = (string)(await c.Call("test.window.open"))["windowId"]!;
        var window = await WindowOf(windowId, c);
        try
        {
        await Task.Delay(3000);
        await c.Call("window.setView", new { windowId, sidePane = new { open = true, page = "files" } });
        await Task.Delay(2500);
        var root = UIA.AutomationElement.FromHandle(window);
        // A row's own name is its item's type name (1.3.7 too); the file name is the text inside it.
        string RowText(UIA.AutomationElement row) =>
            Deep.All(row, new UIA.PropertyCondition(UIA.AutomationElement.ControlTypeProperty, UIA.ControlType.Text)).Select(Deep.Name).FirstOrDefault(n => SortNames.Contains(n)) ?? Deep.Name(row);
        var shown = Deep.All(root, new UIA.PropertyCondition(UIA.AutomationElement.ControlTypeProperty, UIA.ControlType.TreeItem))
            .Select(RowText).Where(n => SortNames.Contains(n)).Distinct().ToList();
        notes.Add("order: " + string.Join(" | ", shown));
        if (shown.Count == 0)
        {
            var all = Deep.All(root, new UIA.PropertyCondition(UIA.AutomationElement.ControlTypeProperty, UIA.ControlType.TreeItem)).Select(Deep.Name).Take(12).ToList();
            notes.Add($"tree rows ({all.Count}): " + string.Join(" | ", all));
            notes.Add("work folder: " + (await c.Call("test.settings.get", new { windowId, name = "StartupOpenFolder" }))["value"] + ", action " + (await c.Call("test.settings.get", new { windowId, name = "FolderStartupAction" }))["value"]);
        }
        Check(shown.Count == SortNames.Length, $"all {SortNames.Length} files are in the tree ({shown.Count})");
        if (SortExpected.Length > 0)
            Check(shown.SequenceEqual(SortExpected), "the order is 1.3.7's: " + string.Join(" | ", SortExpected));
        }
        finally
        {
            await c.Call("test.settings.set", new { windowId = first, name = "FolderStartupAction", value = action });
            await c.Call("test.settings.set", new { windowId = first, name = "StartupOpenFolder", value = folder });
            PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
            await Task.Delay(1500);
        }
    }
}
