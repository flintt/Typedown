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
    /// <summary>
    /// FT03: the same after the other ways a folder's row comes about: a big folder expanded and the root changed under
    /// it, a folder moved in while the tree shows the root (a sync client fetching it), and one made under a temporary
    /// name and renamed (how a sync client usually writes it). Each folder made here has a file in it, so its row expands.
    /// </summary>
    private static async Task FT03(List<string> notes)
    {
        using var c = await Session("e2e FT03");
        var top = Path.Combine(fixtures, "ft03");
        if (Directory.Exists(top)) Directory.Delete(top, true);
        var root = Path.Combine(top, "root");
        var outside = Path.Combine(top, "outside");
        for (var i = 0; i < 8; i++)
        {
            Directory.CreateDirectory(Path.Combine(root, $"dir-{i:00}"));
            File.WriteAllText(Path.Combine(root, $"dir-{i:00}", "inner.md"), "# inner\n");
        }
        var big = Path.Combine(root, "big");
        Directory.CreateDirectory(big);
        for (var i = 0; i < 30; i++) File.WriteAllText(Path.Combine(big, $"file-{i:00}.md"), $"# file {i}\n");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.md"), "# keep\n");
        var doc = await Open(c, Fixture("ft03.md", "# FT03\n"));
        var windowId = await WindowIdOf(c, doc);
        var window = await WindowOf(windowId, c);
        async Task<bool> RootIs(string folder) => await Eventually(async () => SamePath((string?)(await FolderState(c, windowId))["root"], folder), 5000);

        List<(string Name, bool Leaf, AutomationElement Row)> Rows() =>
            Deep.All(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem))
                .Cast<AutomationElement>()
                .Select(e =>
                {
                    var leaf = !e.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var p)
                        || ((ExpandCollapsePattern)p).Current.ExpandCollapseState == ExpandCollapseState.LeafNode;
                    var text = e.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
                    return (text?.Current.Name ?? e.Current.Name, leaf, e);
                }).ToList();

        // Every row named like a folder made here expands; the names expected are all there.
        async Task CheckFolders(string what, params string[] expected)
        {
            List<(string Name, bool Leaf, AutomationElement Row)> rows = new();
            for (var i = 0; i < 40; i++)
            {
                await Task.Delay(200);
                rows = Rows();
                if (expected.All(n => rows.Any(r => r.Name == n)) && rows.Where(r => r.Name.StartsWith("dir-") || expected.Contains(r.Name)).All(r => !r.Leaf)) break;
            }
            var wrong = rows.Where(r => (r.Name.StartsWith("dir-") || expected.Contains(r.Name)) && r.Leaf).Select(r => r.Name).ToList();
            notes.Add($"{what}: {rows.Count} rows, shown as files: {string.Join(", ", wrong)}");
            var missing = expected.Where(n => !rows.Any(r => r.Name == n)).ToList();
            Check(missing.Count == 0, $"{what}: listed ({string.Join(", ", missing)} missing; rows {string.Join(", ", rows.Select(r => r.Name))})");
            Check(wrong.Count == 0, $"{what}: every folder row expands; shown as a file: {string.Join(", ", wrong)}");
        }

        try
        {
            Check((bool)(await c.Call("test.folder.open", new { windowId, path = root }))["opened"]!, "the root opens");
            Check(await RootIs(root), "the tree is rooted there");
            await CheckFolders("at first", "big", "dir-00");

            // The big folder expanded: its thirty files are rows of the tree now. Then up and back, and once more.
            var bigRow = Rows().First(r => r.Name == "big").Row;
            ((ExpandCollapsePattern)bigRow.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
            Check(await Eventually(async () => { await Task.CompletedTask; return Rows().Count(r => r.Name.StartsWith("file-")) > 5; }, 5000), "the big folder's files are shown");
            await Click(window, await WaitFor(window, "FolderUpButton"));
            Check(await RootIs(top), "up to the folder above");
            await CheckFolders("up from the expanded folder", "root", "outside");
            Check((bool)(await c.Call("test.folder.open", new { windowId, path = root }))["opened"]!, "the root opens again");
            Check(await RootIs(root), "back at the root");
            await CheckFolders("back at the root", "big", "dir-00");
            // Folded again, so the rows below it are in view (the tree makes rows only for what is shown).
            var bigAgain = Rows().FirstOrDefault(r => r.Name == "big").Row;
            if (bigAgain != null && bigAgain.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var fold)) ((ExpandCollapsePattern)fold).Collapse();
            Check(await Eventually(async () => { await Task.CompletedTask; return !Rows().Any(r => r.Name.StartsWith("file-")); }, 5000), "the big folder folds");

            // A folder with a file in it moved in from outside while the tree shows the root.
            var fetched = Path.Combine(outside, "fetched");
            Directory.CreateDirectory(fetched);
            File.WriteAllText(Path.Combine(fetched, "a.md"), "# a\n");
            Directory.Move(fetched, Path.Combine(root, "fetched"));
            await CheckFolders("a folder moved in", "fetched");

            // Made under a temporary name with its file, then renamed.
            var temp = Path.Combine(root, "~sync-tmp");
            Directory.CreateDirectory(temp);
            File.WriteAllText(Path.Combine(temp, "b.md"), "# b\n");
            await Task.Delay(300);
            Directory.Move(temp, Path.Combine(root, "synced"));
            await CheckFolders("a folder renamed from a temporary name", "synced");

            // Its attributes touched, as a sync client marks what it has fetched.
            var synced = new DirectoryInfo(Path.Combine(root, "synced"));
            synced.Attributes |= FileAttributes.Archive;
            synced.Attributes |= FileAttributes.NotContentIndexed;
            await CheckFolders("a folder's attributes changed", "synced", "fetched");

            // Up and back with all of that there.
            await Click(window, await WaitFor(window, "FolderUpButton"));
            Check(await RootIs(top), "up again");
            Check((bool)(await c.Call("test.folder.open", new { windowId, path = root }))["opened"]!, "the root opens once more");
            Check(await RootIs(root), "back at the root once more");
            await CheckFolders("back again", "big", "fetched", "synced", "dir-00");
        }
        finally
        {
            try { await c.Call("document.close", new { documentId = doc }); } catch { }
        }
    }
}
