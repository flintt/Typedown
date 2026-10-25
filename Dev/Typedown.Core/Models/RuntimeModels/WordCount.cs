namespace Typedown.Core.Models
{
    /// <summary>
    /// The document's numbers as the editor page counts them (Muya's wordCount), on the text a reader sees: no Markdown
    /// marks, no link addresses. <see cref="Word"/> and <see cref="Character"/> are the status bar's two numbers.
    /// </summary>
    public class WordCount
    {
        /// <summary>Words; each Han character and kana is one.</summary>
        public int Word { get; set; }

        /// <summary>Characters, spaces not counted.</summary>
        public int Character { get; set; }

        public int CharacterWithSpaces { get; set; }

        /// <summary>Han, kana and Hangul characters.</summary>
        public int Cjk { get; set; }

        /// <summary>Words in other scripts (Latin, Cyrillic, Hangul words...).</summary>
        public int OtherWords { get; set; }

        public int Paragraph { get; set; }

        public int Headings { get; set; }

        public int Images { get; set; }

        public int Links { get; set; }

        /// <summary>Minutes to read the prose (about 400 CJK characters and 230 other words a minute; code and formulas left out).</summary>
        public double ReadingMinutes { get; set; }
    }
}
