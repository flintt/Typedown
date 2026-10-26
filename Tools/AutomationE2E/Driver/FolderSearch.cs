using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using Newtonsoft.Json.Linq;

/// <summary>
/// FQ01: the side pane's search through a folder, as a reader uses it. A folder whose files and folders match "kiwi" by
/// name, by content, by both and by neither (with a file too big to read, a dot folder, node_modules and a picture that
/// must stay out); the query typed into the pane's box with real keys, the list read through UI Automation: the two
/// groups, names above contents, each header's count; the names group's five rows and its "show all" row; the marker of
/// the file matching both; each group folded and opened again; a folder result clicked shows the folder open in the tree
/// with the root where it was; a content result clicked opens the file.
/// </summary>
internal static partial class Program
{
    private static async Task FQ01(List<string> notes)
    {
        using var c = await Session("e2e FQ01");
        var root = Path.Combine(fixtures, "fq01");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        void Put(string relative, string text)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        // Names only: kiwi-1..4 and kiwi-plain; both: kiwi-notes; the name and a big body (not read): kiwi-big;
        // content only: apple.md and deep/kiwi-box/inner.md; the folder deep/kiwi-box by its name.
        for (var i = 1; i <= 4; i++) Put($"kiwi-{i}.md", "# nothing to find\n");
        Put("kiwi-plain.md", "# plain\n");
        Put("kiwi-notes.md", "# notes\nA KIWI in the text.\n");
        Put("kiwi-big.md", "kiwi\n" + new string('x', 9 * 1024 * 1024));
        Put("apple.md", "# apple\nthere is a kiwi here\n");
        Put("deep/kiwi-box/inner.md", "kiwi inside\n");
        Put(".kiwi-hidden/kiwi.md", "kiwi\n");
        Put("node_modules/kiwi.md", "kiwi\n");
        Put("kiwi.png", "kiwi");
        string[] names = { "kiwi-1.md", "kiwi-2.md", "kiwi-3.md", "kiwi-4.md", "kiwi-big.md", "kiwi-notes.md", "kiwi-plain.md", "deep/kiwi-box" };
        string[] contents = { "apple.md", "deep/kiwi-box/inner.md" };

        var doc = await Open(c, Fixture("fq01.md", "# FQ01\n"));
        var windowId = await WindowIdOf(c, doc);
        var window = await WindowOf(windowId, c);
        try
        {
            Check((bool)(await c.Call("test.folder.open", new { windowId, path = root }))["opened"]!, "the fixture folder opens as the root");
            await c.Call("window.setView", new { windowId, sidePane = new { open = true, page = "files" } });
            Check(await Eventually(async () => SamePath((string?)(await FolderState(c, windowId))["root"], root), 5000), "the tree is rooted at the fixture folder");

            // An element is there for UI Automation as soon as it is made, before it has a place on the screen: the
            // search box reopened was clicked at that moment ("not on screen", w10). Waited for until it has one.
            async Task<AutomationElement> OnScreen(string id, string what)
            {
                var element = await WaitFor(window, id);
                if (!await Eventually(async () => { await Task.CompletedTask; var r = element.Current.BoundingRectangle; return !r.IsEmpty && r.Width > 0 && r.Height > 0; }, 5000))
                    throw new CaseFailed($"{what} ({id}) is there but has no place on the screen after 5 s ({FocusInfo(window)})");
                return element;
            }
            async Task Search()
            {
                await Click(window, await OnScreen("SearchButton", "the side pane's search button"));
                var box = await OnScreen("SearchTextBox", "the search box");
                await Click(window, box);
                await Task.Delay(300);
                TypeText("kiwi");
            }
            AutomationElement? Row(string group, string path) => FindIn(window, $"{group}:{path}");
            bool AllThere(string group, IEnumerable<string> paths) => paths.All(p => Row(group, p) != null);

            await Search();
            // The contents arrive last: once both are there, the search is through.
            Check(await Eventually(async () => { await Task.CompletedTask; return AllThere("SearchContent", contents); }, 15000),
                $"the content results are listed ({FocusInfo(window)})");
            var namesGroup = await WaitFor(window, "SearchNamesGroup");
            var contentsGroup = await WaitFor(window, "SearchContentsGroup");
            notes.Add($"headers: '{namesGroup.Current.Name}' / '{contentsGroup.Current.Name}'");
            Check(namesGroup.Current.Name.Contains($"({names.Length})"), $"the names header counts {names.Length}: '{namesGroup.Current.Name}'");
            Check(contentsGroup.Current.Name.Contains($"({contents.Length})"), $"the contents header counts {contents.Length}: '{contentsGroup.Current.Name}'");
            Check(namesGroup.Current.BoundingRectangle.Top < contentsGroup.Current.BoundingRectangle.Top, "the names group is above the contents group");

            // Five name rows, in the walk's order (top level by name, then the folder below), and the "show all" row.
            var shown = names.Take(5).ToArray();
            Check(AllThere("SearchName", shown), "the first five name matches are listed: " + string.Join(", ", shown.Where(p => Row("SearchName", p) == null)));
            Check(names.Skip(5).All(p => Row("SearchName", p) == null), "the names past the fifth wait for \"show all\"");
            var tops = shown.Select(p => Row("SearchName", p)!.Current.BoundingRectangle.Top).ToList();
            Check(tops.SequenceEqual(tops.OrderBy(t => t)), "the name rows are in order: " + string.Join(", ", tops));
            var showAll = await WaitFor(window, "SearchShowAllNames");
            notes.Add($"show all: '{showAll.Current.Name}'");
            Check(showAll.Current.Name.Contains(names.Length.ToString()), $"the show-all row says {names.Length}: '{showAll.Current.Name}'");
            await Click(window, showAll);
            Check(await Eventually(async () => { await Task.CompletedTask; return AllThere("SearchName", names); }, 5000),
                "after \"show all\" every name match is listed: " + string.Join(", ", names.Where(p => Row("SearchName", p) == null)));
            await WaitGone(window, "SearchShowAllNames");

            // Nothing that must stay out, and no file twice.
            foreach (var p in new[] { ".kiwi-hidden", ".kiwi-hidden/kiwi.md", "node_modules/kiwi.md", "kiwi.png" })
                Check(Row("SearchName", p) == null && Row("SearchContent", p) == null, $"{p} is not listed");
            Check(Row("SearchContent", "kiwi-notes.md") == null, "the file matching by name and content is not in the contents group");
            var lastName = names.Max(p => Row("SearchName", p)!.Current.BoundingRectangle.Bottom);
            Check(contents.All(p => Row("SearchContent", p)!.Current.BoundingRectangle.Top > lastName), "every content row is below every name row");

            // The marker: on the file matching both, not on a name-only file or the big one (its body is not read).
            bool Marked(string path)
            {
                var row = Row("SearchName", path);
                return row != null && row.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "SearchAlsoContent")) != null;
            }
            Check(await Eventually(async () => { await Task.CompletedTask; return Marked("kiwi-notes.md"); }, 5000), "kiwi-notes.md has the \"content matches too\" marker");
            Check(!Marked("kiwi-plain.md") && !Marked("kiwi-big.md"), "the name-only files and the big one have no marker");
            notes.Add($"kiwi-notes.md read as: '{Row("SearchName", "kiwi-notes.md")!.Current.Name}'");
            // The last status is the summary (the ones before it end in "…").
            var status = "";
            await Eventually(async () => { await Task.CompletedTask; status = FindIn(window, "SearchStatus")?.Current.Name ?? ""; return status.Length > 0 && !status.Contains('…'); }, 10000);
            notes.Add($"status: '{status}'");
            Check(status.Contains("8") && status.Contains("2") && !status.Contains("(max)"), $"the status line says both counts: '{status}'");

            // Folding: the names group through UI Automation, the contents group with a click on its header.
            var fold = (ExpandCollapsePattern)namesGroup.GetCurrentPattern(ExpandCollapsePattern.Pattern);
            Check(fold.Current.ExpandCollapseState == ExpandCollapseState.Expanded, "the names group starts expanded");
            fold.Collapse();
            await WaitGone(window, "SearchName:kiwi-1.md");
            Check(fold.Current.ExpandCollapseState == ExpandCollapseState.Collapsed, "the names group says it is collapsed");
            Check(Row("SearchContent", "apple.md") != null, "the contents group stays open while the names group is folded");
            fold.Expand();
            await WaitFor(window, "SearchName:kiwi-1.md");
            var header = contentsGroup.Current.BoundingRectangle;
            await Click(window, (int)(header.Left + 40), (int)(header.Top + 16));
            await WaitGone(window, "SearchContent:apple.md");
            var contentsFold = (ExpandCollapsePattern)contentsGroup.GetCurrentPattern(ExpandCollapsePattern.Pattern);
            Check(contentsFold.Current.ExpandCollapseState == ExpandCollapseState.Collapsed, "a click on the contents header folds it");
            header = contentsGroup.Current.BoundingRectangle;
            await Click(window, (int)(header.Left + 40), (int)(header.Top + 16));
            await WaitFor(window, "SearchContent:apple.md");

            // A folder result: the pane closes, the tree shows the folder open, the root stays.
            await Click(window, Row("SearchName", "deep/kiwi-box")!);
            await WaitGone(window, "SearchTextBox");
            AutomationElement? TreeItemOf(string name)
            {
                var found = TreeRow(window, name);
                while (found != null && found.Current.ControlType != ControlType.TreeItem) found = TreeWalker.ControlViewWalker.GetParent(found);
                return found;
            }
            ExpandCollapseState? StateOf(string name) =>
                TreeItemOf(name) is { } item && item.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var p) ? ((ExpandCollapsePattern)p).Current.ExpandCollapseState : null;
            Check(await Eventually(async () => { await Task.CompletedTask; return StateOf("kiwi-box") == ExpandCollapseState.Expanded; }, 5000),
                $"the folder is shown open in the tree (deep: {StateOf("deep")}, kiwi-box: {StateOf("kiwi-box")})");
            Check(StateOf("deep") == ExpandCollapseState.Expanded, "the folder above it is open");
            Check(TreeRow(window, "inner.md") != null, "the folder's file is shown under it");
            // Selected, and the keyboard on it (FolderPage.RevealFolder waits for the row to be made).
            bool Selected() => TreeItemOf("kiwi-box") is { } row && row.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var sel) && ((SelectionItemPattern)sel).Current.IsSelected;
            Check(await Eventually(async () => { await Task.CompletedTask; return Selected(); }, 5000), "the folder clicked in the results is the tree's selected row");
            bool Focused()
            {
                try { return TreeItemOf("kiwi-box") is { } row && AutomationElement.FocusedElement is { } focus && Automation.Compare(focus, row); }
                catch (ElementNotAvailableException) { return false; }
            }
            Check(await Eventually(async () => { await Task.CompletedTask; return Focused(); }, 5000), $"the keyboard is on the folder's row in the tree ({FocusInfo(window)}, focused: '{AutomationElement.FocusedElement?.Current.Name}')");
            // The search pane closed for the folder: the next search opens it again, empty (the step below does that).
            Check(FindIn(window, "SearchTextBox") == null, "the search pane is closed after a folder result");
            Check(SamePath((string?)(await FolderState(c, windowId))["root"], root), "the tree's root did not change");

            // A content result: the file opens.
            await Search();
            Check(await Eventually(async () => { await Task.CompletedTask; return Row("SearchContent", "apple.md") != null; }, 15000), "the content results are listed again");
            await Click(window, Row("SearchContent", "apple.md")!);
            var apple = Path.Combine(root, "apple.md");
            Check(await Eventually(async () =>
            {
                var active = (string?)((JArray)(await c.Call("window.list"))["windows"]!).First(w => (string)w["windowId"]! == windowId)["activeDocumentId"];
                var docs = (JArray)(await c.Call("document.list", new { windowId }))["documents"]!;
                return docs.Any(d => (string?)d["documentId"] == active && SamePath((string?)d["path"], apple));
            }, 8000), "clicking the content result opens apple.md");
            Check(FindIn(window, "SearchTextBox") != null, "the search stays open after a file is opened from it");
        }
        finally
        {
            try { await c.Call("window.setView", new { windowId, sidePane = new { open = true, page = "files" } }); } catch { }
            try { await c.Call("document.close", new { documentId = doc }); } catch { }
        }
    }
}
