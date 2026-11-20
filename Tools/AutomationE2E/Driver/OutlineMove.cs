using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;

/// <summary>
/// OL07 (the user's 28, 2026-11-20): a heading dragged in the outline moves its section in the document, as Obsidian does -
/// by real drags with the mouse, checked by the document's text: A dropped below B goes after B's section with its
/// sub-section A1; one Ctrl+Z puts it back; B dropped onto A goes into A as its last sub-section, a level down; in reading
/// mode a drag moves nothing.
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
        // Dropped onto a row's middle (into it), or below the last row, in the empty space (after it, at the top level): the
        // tree takes a drop on a row's lower edge as into it too (run 679: A became B's sub-section).
        async Task DragRow(string heading, string onto, bool into)
        {
            var from = Row(heading);
            var to = Row(onto);
            var y = into ? to.Top + to.Height / 2 : to.Bottom + 14;
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            await Drag(window, ((int)(from.Left + 40), (int)(from.Top + from.Height / 2)), ((int)(to.Left + 40), (int)y), steps: 20);
            await Task.Delay(800);
        }
        try
        {
            await c.Call("window.setView", new { windowId, mode = "visual", bounds = new { x = 40, y = 40, width = 1300, height = 850 }, sidePane = new { open = true, page = "outline" } });
            await Task.Delay(1200);
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            notes.Add($"rows: A {Row("A")}, A1 {Row("A1")}, B {Row("B")}");

            // A below B (the last row): after B's section, its sub-section with it.
            await DragRow("A", "B", into: false);
            var after = await TextWhen(t => t != original);
            notes.Add("A dropped below B: " + after.Replace("\n", "\\n"));
            Check(after == "# B\n\nb text\n\n# A\n\na text\n\n## A1\n\na1 text\n", "A dropped below B: A's section, A1 with it, after B");

            // One Ctrl+Z: back as it was.
            await c.Call("test.editor.focus", new { windowId });
            await Task.Delay(150);
            Chord("Ctrl+Z");
            var undone = await TextWhen(t => t == original);
            Check(undone == original, "one Ctrl+Z puts the section back");

            // B onto A: into A as its last sub-section, a level down.
            await DragRow("B", "A", into: true);
            var into = await TextWhen(t => t != original);
            notes.Add("B dropped onto A: " + into.Replace("\n", "\\n"));
            Check(into == "# A\n\na text\n\n## A1\n\na1 text\n\n## B\n\nb text\n", "B dropped onto A: A's last sub-section, at level 2");
            var rows = (await c.Call("test.outline.state", new { windowId }))["rows"];
            notes.Add("outline after: " + rows?.ToString(Newtonsoft.Json.Formatting.None));

            // Reading mode: a drag moves nothing.
            await c.Call("test.editor.focus", new { windowId });
            Chord("Ctrl+Z");
            await TextWhen(t => t == original);
            await c.Call("test.settings.set", new { windowId, name = "ReadOnly", value = true });
            await Task.Delay(800);
            await DragRow("A", "B", into: false);
            await Task.Delay(800);
            Check(await Text() == original, "reading mode: a drag moves nothing");
        }
        finally
        {
            try { await c.Call("test.settings.set", new { windowId, name = "ReadOnly", value = false }); } catch { }
            try { await c.Call("document.close", new { documentId = id, discard = true }); } catch { }
        }
    }
}
