using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using Newtonsoft.Json.Linq;

/// <summary>
/// DC01: File > Check Document lists what will not come out right - a picture and a linked file not there, a link to a
/// heading the document lacks, a formula that cannot be drawn - and nothing for what is fine (a picture there, a web
/// link, a heading that exists, code). A row clicked closes the list and finds the place in the text (the find bar).
/// The problems mended, the check finds none.
/// </summary>
internal static partial class Program
{
    private static async Task DC01(List<string> notes)
    {
        using var c = await Session("e2e DC01");
        var folder = Path.Combine(fixtures, "dc01");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "there.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAABAAAAAICAIAAAB/FOjAAAAAE0lEQVR4nGPQqDhBEmIY1UALDQCJj7QBup2ubwAAAABJRU5ErkJggg=="));
        var doc = Path.Combine(folder, "dc01.md");
        var broken = "# Intro\n\n![ok](there.png) ![gone](gone.png)\n\nSee [the notes](notes-gone.md), [the web](https://example.com), [intro](#intro) and [nowhere](#nowhere).\n\n$\\frac{1}{$\n\n`![code](not-checked.png)`\n";
        File.WriteAllText(doc, broken);
        var id = await Open(c, doc);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        List<string> Rows() => FindAnywhereInHost(window, "DocumentCheckList") is { } list
            ? list.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)).Cast<AutomationElement>().Select(e => e.Current.Name).ToList()
            : new List<string>();
        string Summary() => FindAnywhereInHost(window, "DocumentCheckSummary")?.Current.Name ?? "";
        try
        {
            await c.Call("window.setView", new { windowId, mode = "visual" });
            await Task.Delay(1200);
            await ClickViewMenuEntry(window, "CheckDocumentItem");
            var rows = new List<string>();
            await Eventually(() => Task.FromResult((rows = Rows()).Count >= 4), 6000, 200);
            notes.Add("summary: " + Summary());
            notes.Add("rows: " + string.Join(" | ", rows));
            Check(rows.Count == 4, $"four problems listed ({rows.Count})");
            Check(rows.Any(r => r.Contains("gone.png")) && rows.Any(r => r.Contains("notes-gone.md")) && rows.Any(r => r.Contains("#nowhere")),
                "the picture, the linked file and the heading not there");
            Check(rows.Any(r => r.Contains("\\frac")), "and the formula that cannot be drawn");
            Check(!rows.Any(r => r.Contains("there.png") || r.Contains("example.com") || r.Contains("#intro") || r.Contains("not-checked")),
                "nothing for a picture there, a web link, a heading that exists or code");

            // The heading's row clicked: the list goes, the find bar finds the link in the text.
            var row = FindAnywhereInHost(window, "DocumentCheckList")!.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
                .Cast<AutomationElement>().First(e => e.Current.Name.Contains("#nowhere"));
            await ClickInPopup(window, row);
            Check(await Eventually(() => Task.FromResult(FindAnywhereInHost(window, "DocumentCheckList") == null), 3000, 150), "a row clicked closes the list");
            var found = await Eventually(async () => ((string?)(await c.Call("test.editor.eval", new { windowId, script = "document.querySelectorAll('#ag-editor-id .ag-highlight, #ag-editor-id .ag-selection').length" }))["result"] ?? "0") != "0", 4000, 200);
            Check(found, "and the place is found in the text (marked as a search marks it)");
            Send(Key(0x1B, false), Key(0x1B, true));

            // Mended: nothing to report.
            var mended = "# Intro\n\n![ok](there.png)\n\nSee [the web](https://example.com) and [intro](#intro).\n";
            await c.Call("document.replace", new { documentId = id, baseRevision = await Revision(c, id), text = mended, reveal = "document" });
            await Task.Delay(1200);
            await ClickViewMenuEntry(window, "CheckDocumentItem");
            await Eventually(() => Task.FromResult(Summary().Length > 0), 5000, 200);
            notes.Add("mended: " + Summary());
            Check(Rows().Count == 0 && FindAnywhereInHost(window, "DocumentCheckSummary") != null, "mended, the check lists nothing");
            Send(Key(0x1B, false), Key(0x1B, true));
        }
        finally
        {
            await CloseMenus();
            try { await c.Call("document.save", new { documentId = id }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
