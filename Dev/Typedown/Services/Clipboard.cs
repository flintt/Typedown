using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading.Tasks;
using Typedown.Core.Interfaces;
using Typedown.Utilities;

namespace Typedown.Services
{
    public class Clipboard : IClipboard
    {
        public bool ContainsText(TextDataFormat format)
        {
            var view = global::Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            return format switch
            {
                TextDataFormat.Text or TextDataFormat.UnicodeText => view.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text),
                TextDataFormat.Html => view.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.Html),
                TextDataFormat.Rtf => view.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.Rtf),
                _ => false,
            };
        }

        public async Task<string> GetTextAsync(TextDataFormat format)
        {
            if (!ContainsText(format))
            {
                return "";
            }
            var view = global::Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            return format switch
            {
                TextDataFormat.Text or TextDataFormat.UnicodeText => await view.GetTextAsync(),
                TextDataFormat.Html => await view.GetHtmlFormatAsync(),
                TextDataFormat.Rtf => await view.GetRtfAsync(),
                _ => throw new NotImplementedException(),
            };
        }

        public async Task SetFileDropListAsync(StringCollection fileDropList)
        {
            var dataPackage = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
            var storageItems = new List<global::Windows.Storage.IStorageItem>();
            foreach (string filePath in fileDropList)
            {
                var file = await global::Windows.Storage.StorageFile.GetFileFromPathAsync(filePath);
                storageItems.Add(file);
            }
            dataPackage.SetStorageItems(storageItems);
            SetContent(dataPackage);
        }

        public async Task<StringCollection> GetFileDropListAsync()
        {
            var view = global::Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (view.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                var storageItems = await view.GetStorageItemsAsync();
                var fileDropList = new StringCollection();
                foreach (var item in storageItems)
                {
                    fileDropList.Add(item.Path);
                }
                return fileDropList;
            }
            return new StringCollection();
        }

        public async Task<IClipboardImage> GetImageAsync()
        {
            var view = global::Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (view.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.Bitmap))
            {
                var bitmapStreamRef = await view.GetBitmapAsync();
                return new ClipboardImage(bitmapStreamRef);
            }
            return null;
        }

        /// <summary>
        /// The clipboard written, tried again a few times while another program holds it (CLIPBRD_E_CANT_OPEN: a clipboard
        /// manager, a remote desktop session reading it): Copy and Cut did nothing then. Throws if it stays held.
        /// </summary>
        private static void SetContent(global::Windows.ApplicationModel.DataTransfer.DataPackage dataPackage)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
                    return;
                }
                catch (System.Runtime.InteropServices.COMException ex) when (attempt < 6)
                {
                    Core.Utilities.Log.Debug($"clipboard: held by another program ({ex.HResult:x8}), attempt {attempt}");
                    System.Threading.Thread.Sleep(40 * attempt);
                }
            }
        }

        public void SetTextAndHtml(string text, string html)
        {
            var dataPackage = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
            if (!string.IsNullOrEmpty(text)) dataPackage.SetText(text);
            // CF_HTML: the fragment with the header Word and the browsers read it by.
            if (!string.IsNullOrEmpty(html)) dataPackage.SetHtmlFormat(global::Windows.ApplicationModel.DataTransfer.HtmlFormatHelper.CreateHtmlFormat(html));
            SetContent(dataPackage);
        }

        // A bitmap (Windows offers it as CF_BITMAP, CF_DIB and CF_DIBV5: what Word, OneNote, mail and the chat programs
        // paste as a picture) and the text (what Notepad and code editors paste), in one item. No HTML with the picture in
        // it: a program that takes HTML before a bitmap (Word does) would then depend on reading a data: picture there.
        // Flushed, so the picture stays on the clipboard when the app is closed and another program asking for it does
        // not wait on this window to draw it.
        public async Task SetTextAndPngAsync(string text, byte[] png)
        {
            var dataPackage = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
            if (!string.IsNullOrEmpty(text)) dataPackage.SetText(text);
            if (png is { Length: > 0 })
            {
                var stream = new global::Windows.Storage.Streams.InMemoryRandomAccessStream();
                using (var writer = new global::Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(png);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                    writer.DetachStream();
                }
                stream.Seek(0);
                dataPackage.SetBitmap(global::Windows.Storage.Streams.RandomAccessStreamReference.CreateFromStream(stream));
                // Word takes plain text over a bitmap, and pasted the diagram's code; it takes RTF over both, so the picture
                // goes in as RTF too (Word, WordPad, Outlook). Places with text only still get the code.
                var rtf = PngRtf(png);
                if (rtf != null) dataPackage.SetRtf(rtf);
            }
            SetContent(dataPackage);
            try
            {
                global::Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            }
            catch (Exception ex)
            {
                Core.Utilities.Log.Debug($"clipboard: not flushed ({ex.HResult:x8}); the copy stays while the app runs");
            }
        }

        /// <summary>
        /// A PNG as an RTF picture, shown at half its pixels (the diagram copy draws at 2x); null if it is not a PNG.
        /// </summary>
        internal static string PngRtf(byte[] png)
        {
            if (png == null || png.Length < 24 || png[0] != 0x89 || png[1] != (byte)'P' || png[2] != (byte)'N' || png[3] != (byte)'G') return null;
            int width = png[16] << 24 | png[17] << 16 | png[18] << 8 | png[19];
            int height = png[20] << 24 | png[21] << 16 | png[22] << 8 | png[23];
            if (width <= 0 || height <= 0) return null;
            // Twips at 96 dpi are 15 a pixel; half the pixels for the 2x drawing.
            var rtf = new System.Text.StringBuilder(png.Length * 2 + 200);
            rtf.Append(@"{\rtf1\ansi{\pict\pngblip\picw").Append(width).Append(@"\pich").Append(height)
               .Append(@"\picwgoal").Append(width * 15 / 2).Append(@"\pichgoal").Append(height * 15 / 2).Append(' ');
            rtf.Append(Convert.ToHexString(png));
            rtf.Append("}}");
            return rtf.ToString();
        }

        public void SetText(string text, TextDataFormat format)
        {
            var dataPackage = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
            switch (format)
            {
                case TextDataFormat.Text:
                case TextDataFormat.UnicodeText:
                    dataPackage.SetText(text);
                    break;
                case TextDataFormat.Html:
                    dataPackage.SetHtmlFormat(text);
                    break;
                case TextDataFormat.Rtf:
                    dataPackage.SetRtf(text);
                    break;
                default:
                    dataPackage.SetText(text);
                    break;
            }
            SetContent(dataPackage);
        }

        public void SetText(string text)
        {
            var dataPackage = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(text);
            SetContent(dataPackage);
        }
    }
}