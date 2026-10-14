namespace Typedown.Core.Utilities
{
    /// <summary>Text typed on the home page, as its blank document takes it (HomePage hands it to the editor's paste).</summary>
    public static class TypedText
    {
        /// <summary>
        /// Enter is a new paragraph in the visual editor (a blank line in Markdown) and a line break in source mode;
        /// a tab is not text here (it moved the keyboard on).
        /// </summary>
        public static string AsDocumentText(string typed, bool sourceMode)
        {
            var text = (typed ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", string.Empty);
            return sourceMode ? text : text.Replace("\n", "\n\n");
        }
    }
}
