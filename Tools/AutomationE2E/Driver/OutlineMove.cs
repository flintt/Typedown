using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;

/// <summary>
/// OL07 (the user's 28, 2026-11-20): a heading dragged in the outline moves its section in the document, as Obsidian does -
/// by real drags with the mouse, checked by the document's text. Where on a row the drop is says where it goes: the row's
/// upper third before it, its middle into it (its last sub-section, a level down), its lower third after it (an open row:
/// before its first sub-section, where the line shows), below the last row after the last section. One Ctrl+Z puts each
/// back. In reading mode a drag moves nothing.
/// </summary>
internal static partial class Program
{
    private static async Task OL07(List<string> notes)
    {
        const string original = "# A\n\na text\n\n## A1\n\na1 text\n\n# B\n\nb text\n";
        using var c = await Session("e2e OL07");
        var id = await Open(c, Fixture("ol07.md", original));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        // An outline row by its heading's text, as UI Automation has it (screen pixels: the driver is DPI-aware).
        System.Windows.Rect Row(string heading)
        {
            foreach (AutomationElement item in Deep.All(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem)))
            {
                var text = Deep.First(item, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
                if (text?.Current.Name == heading) return item.Current.BoundingRectangle;
            }
            throw new CaseFailed($"no outline row '{heading}' {Screenshot("OL07-no-row")}");
        }
        async Task<string> Text() => (string)(await Get(c, id))["text"]!;
        async Task<string> TextWhen(Func<string, bool> holds)
        {
            var t = "";
            await Eventually(async () => holds(t = await Text()), 5000, 200);
            return t;
        }
        // A heading's row dragged to a point on another row (0 its top, 1 its bottom, more is below it), the text noted with
        // what the host asked of the page.
        async Task<string> DragRow(string heading, string onto, double at)
        {
            var from = Row(heading);
            var to = Row(onto);
            var asked = LogLines("outline: ").Count;
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            await Drag(window, ((int)(from.Left + 40), (int)(from.Top + from.Height / 2)), ((int)(to.Left + 40), (int)(to.Top + to.Height * at)), steps: 20);
            var text = await TextWhen(t => t != original);
            await Task.Delay(500);
            // The drag as the host logged it: begun, each place it showed, the drop.
            var logged = LogLines("outline: ").Skip(asked).Select(l => l[Math.Max(0, l.IndexOf("outline:", StringComparison.Ordinal))..]).ToList();
            notes.Add($"{heading} dropped at {at:0.##} of {onto}'s row: {text.Replace("\n", "\\n")}; host: {(logged.Count > 0 ? string.Join(" | ", logged) : "(nothing)")}");
            return text;
        }
        async Task Undo(string what)
        {
            await c.Call("test.editor.focus", new { windowId });
            await Task.Delay(150);
            Chord("Ctrl+Z");
            Check(await TextWhen(t => t == original) == original, $"{what}: one Ctrl+Z puts it back");
            await Task.Delay(600);
        }
        try
        {
            await c.Call("window.setView", new { windowId, mode = "visual", bounds = new { x = 40, y = 40, width = 1300, height = 850 }, sidePane = new { open = true, page = "outline" } });
            await Task.Delay(1200);
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            notes.Add($"rows: A {Row("A")}, A1 {Row("A1")}, B {Row("B")}");

            // Where on a row the drop is says where the section goes (runs 679-681: the tree's own reordering put every drop
            // into the row). A is open, with A1 under it.
            var cases = new (string drag, string onto, double at, string expected, string what)[]
            {
                ("B", "A", 0.15, "# B\n\nb text\n\n# A\n\na text\n\n## A1\n\na1 text\n", "B on A's upper third: before A"),
                ("B", "A", 0.5, "# A\n\na text\n\n## A1\n\na1 text\n\n## B\n\nb text\n", "B on A's middle: into A, its last sub-section a level down"),
                ("B", "A", 0.85, "# A\n\na text\n\n## B\n\nb text\n\n## A1\n\na1 text\n", "B on A's lower third (A open): before A1, where the line shows"),
                ("A", "B", 0.85, "# B\n\nb text\n\n# A\n\na text\n\n## A1\n\na1 text\n", "A on B's lower third: after B, A1 with it"),
                ("A", "B", 1.6, "# B\n\nb text\n\n# A\n\na text\n\n## A1\n\na1 text\n", "A below the last row: after B"),
            };
            foreach (var (drag, onto, at, expected, what) in cases)
            {
                var text = await DragRow(drag, onto, at);
                Check(text == expected, what);
                if (text != original) await Undo(what);
            }

            // Reading mode: a drag moves nothing.
            await c.Call("test.settings.set", new { windowId, name = "ReadOnly", value = true });
            await Task.Delay(800);
            await DragRow("A", "B", 0.85);
            Check(await Text() == original, "reading mode: a drag moves nothing");
        }
        finally
        {
            try { await c.Call("test.settings.set", new { windowId, name = "ReadOnly", value = false }); } catch { }
            try { await c.Call("document.close", new { documentId = id, discard = true }); } catch { }
        }
    }
}
