#nullable enable

namespace Typedown.Core.Utilities
{
    /// <summary>What a heading the page names (OutlineCurrent) does to the outline held.</summary>
    public enum OutlineNamed
    {
        /// <summary>Kept for the outline on its way: the one held is another load's, or does not have the heading.</summary>
        Keep,
        /// <summary>The heading marked already, in this load's outline: nothing to do.</summary>
        Already,
        /// <summary>To be marked (it now is the one marked).</summary>
        Mark,
    }

    /// <summary>
    /// The heading the outline marks, and which load's outline it is marked in. Plain .NET, so the rule is tested on
    /// Linux (Dev/Typedown.Automation.Tests/OutlineMarkTests.cs).
    /// </summary>
    public sealed class OutlineMark
    {
        /// <summary>The heading marked, or null.</summary>
        public string? Slug { get; private set; }

        /// <summary>The load whose outline is held, or null before the first.</summary>
        public int? Load { get; private set; }

        /// <summary>
        /// The outline held is now this load's. Another load's outline comes with nothing marked: the heading marked
        /// in the one before is not marked in it, whatever its key.
        /// </summary>
        public void OutlineOf(int load)
        {
            if (Load == load) return;
            Load = load;
            Slug = null;
        }

        /// <summary>The caret's heading (edit mode), marked with the state report that named it.</summary>
        public void Set(string? slug) => Slug = slug;

        /// <summary>A heading the page names for load <paramref name="load"/>; <paramref name="inOutline"/>: the outline held has it.</summary>
        public OutlineNamed Named(int load, string slug, bool inOutline)
        {
            // Only in this load's own outline: one of another load is about to be replaced (OutlineOf), unmarked.
            if (Load != load) return OutlineNamed.Keep;
            if (Slug == slug) return OutlineNamed.Already;
            if (!inOutline) return OutlineNamed.Keep;
            Slug = slug;
            return OutlineNamed.Mark;
        }
    }
}
