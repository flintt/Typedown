using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Typedown.Core.Utilities;
using Xunit;

namespace Typedown.Automation.Tests
{
    /// <summary>Typedown.Core FolderSearch and FolderSearchResults: the side pane's search through a folder, and its two groups.</summary>
    public sealed class FolderSearchTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "td-folder-search-" + Guid.NewGuid().ToString("N"), "plan-root");

        public FolderSearchTests() => Directory.CreateDirectory(root);

        public void Dispose()
        {
            try { Directory.Delete(Path.GetDirectoryName(root)!, true); } catch (IOException) { }
        }

        private string Write(string relative, string content)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        private static string Slashed(FolderSearchHit hit) => hit.RelativePath.Replace('\\', '/');

        private (List<FolderSearchHit> Hits, FolderSearchSummary Summary) Run(string query, FolderSearch? search = null)
        {
            var hits = new List<FolderSearchHit>();
            var summary = (search ?? new FolderSearch()).Run(root, query, hits.Add, CancellationToken.None);
            return (hits, summary);
        }

        [Fact]
        public void Names_come_before_contents_and_a_file_matching_both_is_listed_once_with_the_marker()
        {
            Write("plan.md", "nothing here\n");
            Write("notes.md", "first line\nthe PLAN is this\n");
            Write("plan-b.md", "a plan here\n");
            Write("planning/inner.md", "no\n");
            Write("other/deep.txt", "and a Plan\n");

            var (hits, summary) = Run("plan");

            var lastName = hits.FindLastIndex(h => h.Kind == FolderSearchHitKind.Name);
            var firstContent = hits.FindIndex(h => h.Kind != FolderSearchHitKind.Name);
            Assert.True(lastName < firstContent, "every name before the first content hit: " + string.Join(", ", hits.Select(h => h.Kind + " " + Slashed(h))));
            Assert.Equal(new[] { "plan-b.md", "plan.md", "planning" }, hits.Where(h => h.Kind == FolderSearchHitKind.Name).Select(Slashed));
            Assert.Equal(new[] { "notes.md", "other/deep.txt" }, hits.Where(h => h.Kind == FolderSearchHitKind.Content).Select(Slashed).OrderBy(x => x));
            Assert.Equal("plan-b.md", Assert.Single(hits, h => h.Kind == FolderSearchHitKind.AlsoContent).Name);
            Assert.Equal(new FolderSearchSummary(3, false, 2, false, 5), summary);

            var results = new FolderSearchResults();
            foreach (var hit in hits) results.Apply(hit);
            Assert.Equal(new[] { "plan-b.md", "plan.md", "planning" }, results.ShownNames.Select(Slashed));
            Assert.Equal(new[] { "notes.md", "other/deep.txt" }, results.Contents.Select(Slashed).OrderBy(x => x));
            var both = results.ShownNames.Single(h => h.Name == "plan-b.md");
            Assert.True(both.AlsoContent);
            Assert.True(both.ContentMatched);
            Assert.Equal("a plan here", both.Snippet);
            Assert.False(results.ShownNames.Single(h => h.Name == "plan.md").ContentMatched);
            Assert.Equal("the PLAN is this", results.Contents.Single(h => h.Name == "notes.md").Snippet);
        }

        [Fact]
        public void A_second_root_is_searched_too_its_hits_led_by_its_name_and_a_folder_met_twice_is_searched_once()
        {
            // The tree's folder and, beside it, the document's own folder (not inside the tree's).
            Write("tree-plan.md", "x");
            var other = Path.Combine(Path.GetDirectoryName(root)!, "beside");
            Directory.CreateDirectory(other);
            File.WriteAllText(Path.Combine(other, "plan-beside.md"), "x");
            File.WriteAllText(Path.Combine(other, "notes.md"), "the plan is here");
            var hits = new List<FolderSearchHit>();
            var summary = new FolderSearch().Run(new[] { root, other }, "plan", hits.Add, CancellationToken.None);
            Assert.Equal(new[] { "tree-plan.md", "beside/plan-beside.md" }, hits.Where(h => h.Kind == FolderSearchHitKind.Name).Select(Slashed));
            Assert.Equal(new[] { "beside/notes.md" }, hits.Where(h => h.Kind == FolderSearchHitKind.Content).Select(Slashed));
            Assert.Equal(2, summary.Names);

            // A root inside another is not searched again.
            Write("sub/plan-sub.md", "x");
            var twice = new List<FolderSearchHit>();
            new FolderSearch().Run(new[] { root, Path.Combine(root, "sub") }, "plan-sub", twice.Add, CancellationToken.None);
            Assert.Single(twice);
        }

        [Fact]
        public void Folders_match_by_name_but_the_root_does_not()
        {
            Write("Plans/a.md", "x\n");
            Write("docs/old-plans/b.md", "x\n");
            var (hits, _) = Run("PLAN");
            var names = hits.Where(h => h.Kind == FolderSearchHitKind.Name).ToList();
            Assert.Equal(new[] { "Plans", "docs/old-plans" }, names.Select(Slashed));
            Assert.All(names, h => Assert.True(h.IsFolder));
            Assert.DoesNotContain(hits, h => h.FullPath == root);
        }

        [Fact]
        public void Names_are_matched_at_any_size_and_only_the_content_pass_skips_big_files()
        {
            Write("plan-big.md", new string('x', 200) + " plan\n");
            Write("big-notes.md", new string('x', 200) + " plan\n");
            Write("small.md", "plan\n");
            var (hits, summary) = Run("plan", new FolderSearch { MaxFileBytes = 100 });
            Assert.Equal("plan-big.md", Assert.Single(hits, h => h.Kind == FolderSearchHitKind.Name).Name);
            Assert.DoesNotContain(hits, h => h.Kind == FolderSearchHitKind.AlsoContent);
            Assert.Equal("small.md", Assert.Single(hits, h => h.Kind == FolderSearchHitKind.Content).Name);
            Assert.Equal(1, summary.FilesRead);
        }

        [Fact]
        public void Hidden_entries_dot_folders_node_modules_and_other_file_types_are_left_out()
        {
            Write(".git/plan.md", "plan\n");
            Write(".plan-dir/a.md", "plan\n");
            Write("node_modules/plan.md", "plan\n");
            Write(".plan.md", "plan\n"); // a dot file is hidden (on Windows by its attribute, here by its name)
            Write("plan.png", "plan\n");
            Write("plan.cs", "plan\n");
            Write("keep/plan.markdown", "plan\n");
            Write("keep/notes.log", "plan\n");
            var (hits, _) = Run("plan");
            Assert.Equal(new[] { "keep/plan.markdown" }, hits.Where(h => h.Kind == FolderSearchHitKind.Name).Select(Slashed));
            Assert.Equal(new[] { "keep/notes.log" }, hits.Where(h => h.Kind == FolderSearchHitKind.Content).Select(Slashed));
        }

        [Fact]
        public void Each_group_stops_at_its_own_cap()
        {
            for (var i = 0; i < 5; i++) Write($"plan-{i}.md", "nothing\n");
            for (var i = 0; i < 5; i++) Write($"note-{i}.md", "plan\n");
            var (hits, summary) = Run("plan", new FolderSearch { NameCap = 3, ContentCap = 2 });
            Assert.Equal(3, hits.Count(h => h.Kind == FolderSearchHitKind.Name));
            Assert.Equal(2, hits.Count(h => h.Kind == FolderSearchHitKind.Content));
            Assert.True(summary.NamesAtCap);
            Assert.True(summary.ContentsAtCap);
            Assert.Equal(new FolderSearchSummary(3, true, 2, true, 2), summary);

            var (_, under) = Run("plan", new FolderSearch { NameCap = 6, ContentCap = 6 });
            Assert.False(under.NamesAtCap);
            Assert.False(under.ContentsAtCap);
        }

        [Fact]
        public void A_name_match_past_the_cap_whose_content_matches_is_in_the_contents_group()
        {
            Write("plan-a.md", "nothing\n");
            Write("plan-b.md", "plan\n");
            var (hits, _) = Run("plan", new FolderSearch { NameCap = 1 });
            Assert.Equal("plan-a.md", Assert.Single(hits, h => h.Kind == FolderSearchHitKind.Name).Name);
            Assert.Equal("plan-b.md", Assert.Single(hits, h => h.Kind == FolderSearchHitKind.Content).Name);
        }

        [Fact]
        public void A_cancelled_search_stops_and_reports_no_more()
        {
            Write("plan-1.md", "plan\n");
            Write("plan-2.md", "plan\n");
            Write("notes.md", "plan\n");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => new FolderSearch().Run(root, "plan", _ => throw new Exception("reported"), cancelled.Token));

            // Cancelled at the first name: the names pass stops there and the contents pass never starts.
            using var typing = new CancellationTokenSource();
            var hits = new List<FolderSearchHit>();
            var namesDone = false;
            Assert.ThrowsAny<OperationCanceledException>(() => new FolderSearch().Run(root, "plan", h => { hits.Add(h); typing.Cancel(); }, typing.Token, _ => namesDone = true));
            Assert.Single(hits);
            Assert.False(namesDone);
        }

        [Fact]
        public void The_names_group_shows_five_until_show_all_and_a_new_query_goes_back_to_five()
        {
            var results = new FolderSearchResults();
            Assert.True(results.NamesExpanded);
            Assert.True(results.ContentsExpanded);
            FolderSearchHit Name(int i) => new() { Kind = FolderSearchHitKind.Name, FullPath = $"/r/plan-{i}.md", Name = $"plan-{i}.md", RelativePath = $"plan-{i}.md" };
            FolderSearchHit Content(int i) => new() { Kind = FolderSearchHitKind.Content, FullPath = $"/r/n-{i}.md", Name = $"n-{i}.md", RelativePath = $"n-{i}.md" };

            for (var i = 0; i < 5; i++) results.Apply(Name(i));
            Assert.False(results.CanShowAllNames);
            for (var i = 5; i < 8; i++) results.Apply(Name(i));
            for (var i = 0; i < 12; i++) results.Apply(Content(i));
            Assert.Equal(8, results.NameCount);
            Assert.Equal(5, results.ShownNames.Count);
            Assert.True(results.CanShowAllNames);
            Assert.Equal(12, results.Contents.Count); // the contents group shows all as found

            results.ShowAllNames();
            Assert.Equal(Enumerable.Range(0, 8).Select(i => $"plan-{i}.md"), results.ShownNames.Select(h => h.Name));
            Assert.False(results.CanShowAllNames);
            results.Apply(Name(8)); // found after "show all": shown at once
            Assert.Equal(9, results.ShownNames.Count);

            // Folded groups stay folded for the next query; the names group is back to five.
            results.NamesExpanded = false;
            results.ContentsExpanded = false;
            results.Clear();
            Assert.Equal(0, results.NameCount);
            Assert.Empty(results.ShownNames);
            Assert.Empty(results.Contents);
            for (var i = 0; i < 7; i++) results.Apply(Name(i));
            Assert.Equal(5, results.ShownNames.Count);
            Assert.True(results.CanShowAllNames);
            Assert.False(results.NamesExpanded);
            Assert.False(results.ContentsExpanded);
        }

        [Fact]
        public void The_marker_is_set_once_and_tells_the_list()
        {
            var results = new FolderSearchResults();
            var named = new FolderSearchHit { Kind = FolderSearchHitKind.Name, FullPath = "/r/plan.md", Name = "plan.md", RelativePath = "plan.md" };
            results.Apply(named);
            var changed = new List<string?>();
            named.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            var also = new FolderSearchHit { Kind = FolderSearchHitKind.AlsoContent, FullPath = "/r/plan.md", Name = "plan.md", RelativePath = "plan.md" };
            Assert.Same(named, results.Apply(also));
            Assert.Contains(nameof(FolderSearchHit.AlsoContent), changed);
            Assert.Null(results.Apply(also));
            // A content hit for a file the names group does not list changes nothing there.
            Assert.Null(results.Apply(new FolderSearchHit { Kind = FolderSearchHitKind.AlsoContent, FullPath = "/r/other.md", Name = "other.md", RelativePath = "other.md" }));
            Assert.Empty(results.Contents);
            Assert.Equal(1, results.NameCount);
        }
    }
}
