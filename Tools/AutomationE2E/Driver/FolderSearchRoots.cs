using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Automation;

/// <summary>
/// FQ02: the folder search looks where the reader is. A folder opened in the side pane, then a document opened from
/// another folder beside it: the search finds names in the opened folder and, as well, a file next to the document,
/// led by its folder's name. (It searched the opened folder only, so nothing beside the document was found.)
/// </summary>
internal static partial class Program
{
    private static async Task FQ02(List<string> notes)
    {
        using var c = await Session("e2e FQ02");
        var top = Path.Combine(fixtures, "fq02");
        if (Directory.Exists(top)) Directory.Delete(top, true);
        var tree = Path.Combine(top, "fq02-tree");
        var beside = Path.Combine(top, "fq02-beside");
        Directory.CreateDirectory(tree);
        Directory.CreateDirectory(beside);
        File.WriteAllText(Path.Combine(tree, "start.md"), "# start\n");
        File.WriteAllText(Path.Combine(tree, "mango-tree.md"), "# in the tree\n");
        File.WriteAllText(Path.Combine(beside, "here.md"), "# the document in front\n");
        File.WriteAllText(Path.Combine(beside, "sibling.md"), "# beside it\nA mango on this line.\n");

        var first = await Open(c, Path.Combine(tree, "start.md"));
        var windowId = await WindowIdOf(c, first);
        var window = await WindowOf(windowId, c);
        string? here = null;
        try
        {
            Check((bool)(await c.Call("test.folder.open", new { windowId, path = tree }))["opened"]!, "the tree folder opens as the root");
            await c.Call("window.setView", new { windowId, sidePane = new { open = true, page = "files" } });
            Check(await Eventually(async () => SamePath((string?)(await FolderState(c, windowId))["root"], tree), 5000), "the tree is rooted at the opened folder");
            here = await Open(c, Path.Combine(beside, "here.md"));
            Check(await WindowIdOf(c, here) == windowId, "the document from beside opens in the same window");
            Check(await Eventually(async () => SamePath((string?)(await FolderState(c, windowId))["root"], tree), 3000), "an opened folder stays the root when a document from elsewhere is opened");

            // The search pane may still be open from the case before (FQ01 leaves it open): then its box is used.
            if (FindIn(window, "SearchTextBox") == null) await Click(window, await WaitFor(window, "SearchButton"));
            var box = await WaitFor(window, "SearchTextBox");
            await Eventually(async () => { await Task.CompletedTask; var r = box.Current.BoundingRectangle; return !r.IsEmpty && r.Width > 0; }, 5000);
            await Click(window, box);
            await Task.Delay(300);
            Keys(("ctrl", (ushort)0x41));
            TypeText("mango");

            AutomationElement? Row(string id) => FindIn(window, id);
            // Waited for by its own results: the status line can still be the case before's summary.
            Check(await Eventually(async () => { await Task.CompletedTask; return Row("SearchName:mango-tree.md") != null; }, 15000), "a name in the opened folder is found");
            await Eventually(async () => { await Task.CompletedTask; return Row("SearchContent:fq02-beside/sibling.md") != null; }, 15000);
            notes.Add($"status: '{FindIn(window, "SearchStatus")?.Current.Name}'");
            Check(Row("SearchContent:fq02-beside/sibling.md") != null, $"the file beside the document is found, led by its folder's name ({FocusInfo(window)})");
            Check(Row("SearchContent:sibling.md") == null, "the file beside the document is not shown as if it were in the opened folder");
        }
        finally
        {
            try { await c.Call("window.setView", new { windowId, sidePane = new { open = true, page = "files" } }); } catch { }
            if (here != null) try { await c.Call("document.close", new { documentId = here }); } catch { }
            try { await c.Call("document.close", new { documentId = first }); } catch { }
        }
    }
}
