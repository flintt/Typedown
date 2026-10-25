using Typedown.Core.Utilities;
using Xunit;

namespace Typedown.Automation.Tests
{
    /// <summary>
    /// Typedown.Core OutlineMark: the heading the page names for a load is marked in that load's outline. The page numbers
    /// its blocks from the start each time it is loaded (start-up, leaving the settings), so a document shown again names
    /// the very key it named before - the heading the host had marked, in the outline of the load before.
    /// </summary>
    public class OutlineMarkTests
    {
        [Fact]
        public void The_key_marked_for_the_load_before_is_marked_again_in_the_new_outline()
        {
            var mark = new OutlineMark();
            mark.OutlineOf(1);
            Assert.Equal(OutlineNamed.Mark, mark.Named(1, "ag-5", true));
            // The page loaded again: its outline comes first, then the heading at the top of the window, the same key.
            mark.OutlineOf(2);
            Assert.Equal(OutlineNamed.Mark, mark.Named(2, "ag-5", true));
            Assert.Equal("ag-5", mark.Slug);
        }

        [Fact]
        public void A_heading_named_before_its_outline_arrives_is_kept_for_it_even_when_the_old_outline_has_that_key()
        {
            var mark = new OutlineMark();
            mark.OutlineOf(1);
            Assert.Equal(OutlineNamed.Mark, mark.Named(1, "ag-5", true));
            // Load 2's heading arrives while load 1's outline is still held - which has a heading with that key too.
            Assert.Equal(OutlineNamed.Keep, mark.Named(2, "ag-5", true));
            // Load 2's outline: the kept heading is marked in it.
            mark.OutlineOf(2);
            Assert.Equal(OutlineNamed.Mark, mark.Named(2, "ag-5", true));
        }

        [Fact]
        public void The_same_heading_again_in_the_same_outline_is_marked_already()
        {
            var mark = new OutlineMark();
            mark.OutlineOf(3);
            Assert.Equal(OutlineNamed.Mark, mark.Named(3, "ag-7", true));
            Assert.Equal(OutlineNamed.Already, mark.Named(3, "ag-7", true));
            // An edit's outline of the same load keeps the mark.
            mark.OutlineOf(3);
            Assert.Equal(OutlineNamed.Already, mark.Named(3, "ag-7", true));
        }

        [Fact]
        public void The_carets_heading_counts_as_marked()
        {
            var mark = new OutlineMark();
            mark.OutlineOf(4);
            mark.Set("ag-9");
            Assert.Equal(OutlineNamed.Already, mark.Named(4, "ag-9", true));
        }

        [Fact]
        public void A_heading_not_in_the_outline_or_before_any_outline_is_kept()
        {
            var mark = new OutlineMark();
            Assert.Equal(OutlineNamed.Keep, mark.Named(1, "ag-1", false));
            mark.OutlineOf(1);
            Assert.Equal(OutlineNamed.Keep, mark.Named(1, "ag-2", false));
            Assert.Null(mark.Slug);
        }
    }
}
