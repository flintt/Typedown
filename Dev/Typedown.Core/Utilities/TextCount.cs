using System.Globalization;
using System.Text;

namespace Typedown.Core.Utilities
{
    /// <summary>
    /// Words and characters of plain text, counted as the editor page counts a document (Muya's wordCount.js): each Han
    /// character and kana is a word, other words are what stands between spaces with a letter or digit in it, and
    /// characters are all but whitespace. Used for the selection, which the page sends as the text a reader sees.
    /// </summary>
    public static class TextCount
    {
        public static (int Words, int Characters) Count(string text)
        {
            if (string.IsNullOrEmpty(text)) return (0, 0);
            int words = 0, characters = 0;
            var inWord = false;
            var wordHasLetter = false;
            void EndWord()
            {
                if (inWord && wordHasLetter) words++;
                inWord = wordHasLetter = false;
            }
            foreach (var rune in text.EnumerateRunes())
            {
                if (Rune.IsWhiteSpace(rune)) { EndWord(); continue; }
                characters++;
                if (IsCharacterWord(rune.Value)) { EndWord(); words++; continue; }
                inWord = true;
                if (Rune.IsLetterOrDigit(rune)) wordHasLetter = true;
            }
            EndWord();
            return (words, characters);
        }

        /// <summary>Han (every block) and kana, with the kana's long mark: read a character at a time.</summary>
        private static bool IsCharacterWord(int c) =>
            (c >= 0x2E80 && c <= 0x2FDF) || c == 0x3005 || c == 0x3007 || (c >= 0x3021 && c <= 0x3029) || (c >= 0x3038 && c <= 0x303B)
            || (c >= 0x3041 && c <= 0x309F) || (c >= 0x30A0 && c <= 0x30FF) || (c >= 0x31F0 && c <= 0x31FF)
            || (c >= 0x3400 && c <= 0x4DBF) || (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0xF900 && c <= 0xFAFF)
            || (c >= 0xFF66 && c <= 0xFF9F) || (c >= 0x20000 && c <= 0x3134F);
    }
}
