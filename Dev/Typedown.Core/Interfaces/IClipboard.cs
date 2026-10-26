using System.Collections.Specialized;
using System.Threading.Tasks;

namespace Typedown.Core.Interfaces
{
    public enum TextDataFormat
    {
        Text,
        UnicodeText,
        Rtf,
        Html,
        CommaSeparatedValue,
        Xaml
    }

    public interface IClipboard
    {
        bool ContainsText(TextDataFormat format);

        Task<string> GetTextAsync(TextDataFormat format);

        void SetText(string text, TextDataFormat format);

        void SetText(string text);

        /// <summary>One copy in two forms: the text, and the same content formatted (HTML) for the programs that take it.</summary>
        void SetTextAndHtml(string text, string html);

        /// <summary>
        /// One copy as a picture and as text (a diagram's copy button): the picture for the programs that take one (Word,
        /// OneNote, mail, chat), the text for the rest. <paramref name="png"/> null: the text alone. A host that cannot
        /// put a picture on its clipboard puts the text.
        /// </summary>
        Task SetTextAndPngAsync(string text, byte[] png)
        {
            SetText(text);
            return Task.CompletedTask;
        }

        Task<StringCollection> GetFileDropListAsync();

        Task SetFileDropListAsync(StringCollection fileDropList);

        Task<IClipboardImage> GetImageAsync();
    }

    public interface IClipboardImage
    {
        void SaveAsPng(string path);

        byte[] GetBytes();
    }
}
