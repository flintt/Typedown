using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Newtonsoft.Json.Linq;
using Typedown.Core.Utilities;
using Xunit;

namespace Typedown.Automation.Tests
{
    /// <summary>
    /// Typedown.Core FindResultsList and SidePaneReturn: the find bar's results list as the host holds it (the page's
    /// FindState and FindResults), and where the side pane goes back to when the list closes.
    /// </summary>
    public sealed class FindResultsTests
    {
        private static JObject List(int version, int total, int index, params (string Text, int Start, int End)[] entries) => new()
        {
            ["version"] = version,
            ["total"] = total,
            ["index"] = index,
            ["capped"] = total > entries.Length,
            ["entries"] = new JArray(entries.Select(e => new JObject { ["text"] = e.Text, ["start"] = e.Start, ["end"] = e.End })),
        };

        [Fact]
        public void A_list_from_the_page_is_cut_into_the_text_before_the_match_and_after()
        {
            var results = new FindResultsList();
            results.Apply(List(3, 2, 1, ("a kiwi here", 2, 6), ("…the kiwi", 5, 9)));
            Assert.Equal(3, results.Version);
            Assert.Equal(2, results.Total);
            Assert.False(results.Capped);
            Assert.Equal(new[] { ("a ", "kiwi", " here"), ("…the ", "kiwi", "") }, results.Entries.Select(e => (e.Before, e.Match, e.After)));
            Assert.Equal(new[] { 0, 1 }, results.Entries.Select(e => e.Index));
            Assert.Equal(1, results.Current);
            Assert.Same(results.Entries[1], results.CurrentEntry);
            Assert.True(results.Entries[1].IsCurrent);
            Assert.False(results.Entries[0].IsCurrent);
        }

        [Fact]
        public void Offsets_out_of_range_are_clamped()
        {
            var entry = FindResultEntry.FromPreview(0, "kiwi", 2, 99);
            Assert.Equal(("ki", "wi", ""), (entry.Before, entry.Match, entry.After));
            entry = FindResultEntry.FromPreview(0, null, 3, 1);
            Assert.Equal("", entry.Text);
        }

        [Fact]
        public void The_count_stays_exact_when_the_page_caps_the_entries()
        {
            var results = new FindResultsList();
            var entries = Enumerable.Range(0, FindResultsList.Cap).Select(i => ($"kiwi {i}", 0, 4)).ToArray();
            results.Apply(List(1, 1200, 0, entries));
            Assert.Equal(1200, results.Total);
            Assert.Equal(FindResultsList.Cap, results.Entries.Count);
            Assert.True(results.Capped);
        }

        [Fact]
        public void The_bar_moving_moves_the_mark_to_its_entry()
        {
            var results = new FindResultsList();
            results.Apply(List(1, 3, 0, ("a", 0, 1), ("b", 0, 1), ("c", 0, 1)));
            var changed = new List<string>();
            ((INotifyPropertyChanged)results).PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            results.ApplyState(new JObject { ["total"] = 3, ["index"] = 2 });
            Assert.Equal(2, results.Current);
            Assert.False(results.Entries[0].IsCurrent);
            Assert.True(results.Entries[2].IsCurrent);
            Assert.Contains(nameof(FindResultsList.Current), changed);
            Assert.Contains(nameof(FindResultsList.CurrentEntry), changed);
            Assert.DoesNotContain(nameof(FindResultsList.Total), changed);
            // A match past the entries shown (over the cap): no entry marked.
            results.SetState(900, 700);
            Assert.Equal(700, results.Current);
            Assert.Null(results.CurrentEntry);
            Assert.DoesNotContain(results.Entries, e => e.IsCurrent);
        }

        [Fact]
        public void Clearing_leaves_no_entries_and_no_match()
        {
            var results = new FindResultsList();
            results.Apply(List(1, 600, 4, ("a", 0, 1)));
            results.Clear();
            Assert.Empty(results.Entries);
            Assert.Equal(0, results.Total);
            Assert.Equal(-1, results.Current);
            Assert.False(results.Capped);
        }

        [Theory]
        [InlineData(false, 0, 0, "")]
        [InlineData(true, -1, 0, "No results")]
        [InlineData(true, 2, 85, "3 of 85")]
        [InlineData(true, -1, 85, "- of 85")]
        public void The_bar_says_where_it_is(bool searching, int current, int total, string expected)
        {
            Assert.Equal(expected, FindResultsList.PositionText("{0} of {1}", "No results", searching, current, total));
        }

        [Fact]
        public void A_screen_reader_hears_the_place_and_the_line()
        {
            var entry = FindResultEntry.FromPreview(11, "…a kiwi here…", 3, 7);
            Assert.Equal("12 of 85: …a kiwi here…", FindResultsList.SpokenName("{0} of {1}", entry, 85));
        }

        [Fact]
        public void Closing_the_list_puts_the_pane_back_as_it_was()
        {
            var pane = new SidePaneReturn();
            Assert.Null(pane.Close());
            // Open on the outline (page 1): back to it, open.
            pane.Open(true, 1);
            Assert.True(pane.Active);
            Assert.Equal((true, 1), pane.Close());
            Assert.False(pane.Active);
            Assert.Null(pane.Close());
            // Closed (on the files page): closed again.
            pane.Open(false, 0);
            Assert.Equal((false, 0), pane.Close());
        }

        [Fact]
        public void Showing_the_list_again_keeps_what_was_there_first()
        {
            var pane = new SidePaneReturn();
            pane.Open(false, 1);
            // "Show all results" again: the pane is open now (for the list), but it was closed before the list.
            pane.Open(true, 0);
            Assert.Equal((false, 1), pane.Close());
        }

        [Fact]
        public void The_reader_taking_the_pane_elsewhere_leaves_nothing_to_go_back_to()
        {
            var pane = new SidePaneReturn();
            pane.Open(false, 1);
            pane.Forget();
            Assert.False(pane.Active);
            Assert.Null(pane.Close());
        }
    }
}
