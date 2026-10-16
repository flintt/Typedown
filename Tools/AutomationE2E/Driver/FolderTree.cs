using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;

/// <summary>
/// FT02: a folder in the file tree is shown as a folder after the tree's root changed. A reader saw a folder listed as a
/// file (a file's icon, no arrow, nothing opened on a click) in the folder above the one they had been in: the listing
/// knew it was a folder, the row did not. Here the root goes from a folder of files up to a folder of folders, and back
/// and forth between the two, and every folder row the tree shows must be one that expands.
/// </summary>
internal static partial class Program
{
    private static async Task FT02(List<string> notes)
    {
        using var c = await Session("e2e FT02");
        var top = Path.Combine(fixtures, "ft02");
        var folders = Path.Combine(top, "folders");
        // Twenty folders, each with a Markdown file (so each has children to expand to); the first holds twenty files.
        for (var i = 0; i < 20; i++)
        {
            var dir = Path.Combine(folders, $"dir-{i:00}");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "inner.md"), "# inner\n");
        }
        var files = Path.Combine(folders, "dir-00");
        for (var i = 0; i < 20; i++) File.WriteAllText(Path.Combine(files, $"file-{i:00}.md"), $"# file {i}\n");
        var doc = await Open(c, Fixture("ft02.md", "# FT02\n"));
        var windowId = await WindowIdOf(c, doc);
        var window = await WindowOf(windowId, c);
        async Task<bool> RootIs(string folder) => await Eventually(async () => SamePath((string?)(await FolderState(c, windowId))["root"], folder), 5000);

        // The tree's rows now: each name and whether it expands (a folder's row has children; a file's is a leaf).
        async Task<List<(string Name, bool Leaf)>> Rows(string expectPrefix)
        {
            List<(string, bool)> rows = new();
            for (var i = 0; i < 30; i++)
            {
                await Task.Delay(200);
                rows = Deep.All(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem))
                    .Cast<AutomationElement>()
                    .Select(e =>
                    {
                        var leaf = !e.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var p)
                            || ((ExpandCollapsePattern)p).Current.ExpandCollapseState == ExpandCollapseState.LeafNode;
                        // The row's own name is its item's type; the name shown is its text.
                        var text = e.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
                        return (text?.Current.Name ?? e.Current.Name, leaf);
                    }).ToList();
                if (rows.Count > 0 && rows.All(r => r.Item1.StartsWith(expectPrefix) || r.Item1.EndsWith(".md"))) break;
            }
            return rows;
        }

        async Task CheckFolders(string what)
        {
            var rows = await Rows("dir-");
            var shown = rows.Where(r => r.Name.StartsWith("dir-")).ToList();
            notes.Add($"{what}: {rows.Count} rows, {shown.Count(r => r.Leaf)} folders shown as leaves");
            Check(shown.Count >= 5, $"{what}: the folders are listed ({string.Join(", ", rows.Select(r => r.Name))})");
            var wrong = shown.Where(r => r.Leaf).Select(r => r.Name).ToList();
            Check(wrong.Count == 0, $"{what}: every folder row expands; shown as a file: {string.Join(", ", wrong)}");
        }

        try
        {
            Check((bool)(await c.Call("test.folder.open", new { windowId, path = files }))["opened"]!, "the folder of files opens");
            Check(await RootIs(files), "the tree is rooted at the folder of files");
            var fileRows = await Rows("file-");
            notes.Add($"the folder of files: {fileRows.Count} rows");

            // Up a level, with the arrow, as the reader went.
            await Click(window, await WaitFor(window, "FolderUpButton"));
            Check(await RootIs(folders), "the up arrow goes to the folder of folders");
            await CheckFolders("after the up arrow");

            // Back and forth a few times: the rows are reused each time the root changes.
            for (var round = 1; round <= 3; round++)
            {
                Check((bool)(await c.Call("test.folder.open", new { windowId, path = files }))["opened"]!, "the folder of files opens again");
                Check(await RootIs(files), "the tree is rooted at the folder of files again");
                await Rows("file-");
                Check((bool)(await c.Call("test.folder.open", new { windowId, path = folders }))["opened"]!, "the folder of folders opens");
                Check(await RootIs(folders), "the tree is rooted at the folder of folders");
                await CheckFolders($"round {round}");
            }
        }
        finally
        {
            try { await c.Call("document.close", new { documentId = doc }); } catch { }
        }
    }
}
