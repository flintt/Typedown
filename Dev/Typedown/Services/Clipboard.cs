using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Typedown.Core.Interfaces;
using Typedown.Core.Utilities;
using Typedown.Utilities;
using DataPackage = global::Windows.ApplicationModel.DataTransfer.DataPackage;

namespace Typedown.Services
{
    public class Clipboard : IClipboard
    {
        public bool ContainsText(TextDataFormat format)
        {
            // Another program holding the clipboard (0x800401D0) throws here too: nothing to paste now, rather than an
            // error dialog for Ctrl+V (the user, 2026-11-22).
            try
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
            catch (Exception ex)
            {
                Core.Utilities.Log.Debug($"clipboard: could not be read ({ex.HResult:x8}): {ex.Message}");
                return false;
            }
        }

        public async Task<string> GetTextAsync(TextDataFormat format)
        {
            if (!ContainsText(format))
            {
                return "";
            }
            try
            {
                var view = global::Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                return format switch
                {
                    TextDataFormat.Text or TextDataFormat.UnicodeText => await view.GetTextAsync(),
                    TextDataFormat.Html => await view.GetHtmlFormatAsync(),
                    TextDataFormat.Rtf => await view.GetRtfAsync(),
                    _ => throw new NotImplementedException(),
                };
            }
            catch (Exception ex) when (ex is not NotImplementedException)
            {
                Core.Utilities.Log.Debug($"clipboard: could not be read ({ex.HResult:x8}): {ex.Message}");
                return "";
            }
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
            var fileDropList = new StringCollection();
            try
            {
                var view = global::Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                if (view.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                {
                    var storageItems = await view.GetStorageItemsAsync();
                    foreach (var item in storageItems)
                    {
                        fileDropList.Add(item.Path);
                    }
                }
            }
            catch (Exception ex)
            {
                Core.Utilities.Log.Debug($"clipboard: could not be read ({ex.HResult:x8}): {ex.Message}");
            }
            return fileDropList;
        }

        public async Task<IClipboardImage> GetImageAsync()
        {
            try
            {
                var view = global::Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                if (view.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.Bitmap))
                {
                    var bitmapStreamRef = await view.GetBitmapAsync();
                    return new ClipboardImage(bitmapStreamRef);
                }
            }
            catch (Exception ex)
            {
                Core.Utilities.Log.Debug($"clipboard: could not be read ({ex.HResult:x8}): {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// The clipboard written, tried again while another program holds it (CLIPBRD_E_CANT_OPEN - 0x800401D0 "OpenClipboard
        /// 失败": a clipboard manager, a remote desktop session reading it): Copy and Cut did nothing then, and the last
        /// attempt ended in an error dialog over the document (the user, 2026-11-22). Tried at once a few times, then in the
        /// background for a few seconds: a copy lands as soon as the other program lets the clipboard go, and one that never
        /// lands is written in the log, with nothing shown.
        /// </summary>
        private void SetContent(DataPackage dataPackage)
        {
            Exception held = null;
            for (var attempt = 1; attempt <= ImmediateWrites; attempt++)
            {
                try
                {
                    global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
                    ClearPending();
                    return;
                }
                catch (Exception ex)
                {
                    if (!HeldByAnotherProgram(ex))
                    {
                        // Not the clipboard's being held (the data itself refused, say): said in the log, as before.
                        Core.Utilities.Log.Debug($"clipboard: not written ({ex.HResult:x8}): {ex.Message}");
                        return;
                    }
                    held = ex;
                    if (attempt < ImmediateWrites) System.Threading.Thread.Sleep(20 * attempt);
                }
            }
            KeepForASecondTry(dataPackage, held);
        }

        /// <summary>Writes tried at once, with a short wait between them: the holds that are over in a moment (a copy made by another program just now).</summary>
        private const int ImmediateWrites = 3;

        /// <summary>How long a write held by another program is tried again in the background, and how often.</summary>
        private static readonly TimeSpan RetryFor = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan RetryEvery = TimeSpan.FromMilliseconds(120);

        /// <summary>HRESULT of CLIPBRD_E_CANT_OPEN ("OpenClipboard 失败"): the clipboard is open in another program.</summary>
        private const int ClipbrdCannotOpen = unchecked((int)0x800401D0);

        private static bool HeldByAnotherProgram(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
                if (e.HResult == ClipbrdCannotOpen) return true;
            return false;
        }

        // The write not made: the last one only (a later copy that lands takes its place, from any window - the clipboard is
        // one), with the moment it stops being tried.
        private static readonly object retryGate = new();
        private static DataPackage pendingContent;
        private static DateTime pendingUntil;
        private static bool retrying;
        private DispatcherQueue dispatcher;
        private DispatcherQueueTimer retryTimer;

        private void KeepForASecondTry(DataPackage dataPackage, Exception error)
        {
            lock (retryGate)
            {
                pendingContent = dataPackage;
                pendingUntil = DateTime.UtcNow.Add(RetryFor);
                if (retrying) return;
                retrying = true;
            }
            Core.Utilities.Log.Debug($"clipboard: held by another program ({error.HResult:x8}); tried again in the background for {RetryFor.TotalSeconds:0} s");
            var queue = dispatcher ??= DispatcherQueue.GetForCurrentThread();
            if (queue == null)
            {
                // No window thread to go back to (a call from nowhere the window could be): the copy is not made.
                lock (retryGate) { pendingContent = null; retrying = false; }
                return;
            }
            if (retryTimer == null)
            {
                retryTimer = queue.CreateTimer();
                retryTimer.IsRepeating = true;
                retryTimer.Interval = RetryEvery;
                retryTimer.TickSafe((_, _) => RetryWrite());
            }
            retryTimer.Start();
        }

        // A copy written (or replaced by another): nothing left to retry.
        private void ClearPending()
        {
            lock (retryGate)
            {
                pendingContent = null;
                retrying = false;
            }
            retryTimer?.Stop();
        }

        private void RetryWrite()
        {
            DataPackage dataPackage;
            lock (retryGate) dataPackage = pendingContent;
            if (dataPackage == null)
            {
                retryTimer?.Stop();
                return;
            }
            if (DateTime.UtcNow > pendingUntil)
            {
                lock (retryGate)
                {
                    if (pendingContent == dataPackage) pendingContent = null;
                    retrying = false;
                }
                retryTimer?.Stop();
                Core.Utilities.Log.Debug("clipboard: still held by another program; the copy was not made");
                return;
            }
            try
            {
                global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
                lock (retryGate)
                {
                    if (pendingContent == dataPackage) pendingContent = null;
                    retrying = false;
                }
                retryTimer?.Stop();
                Core.Utilities.Log.Debug("clipboard: the copy written once the other program let the clipboard go");
            }
            catch (Exception ex) when (HeldByAnotherProgram(ex))
            {
                // Still held: looked at again on the next tick.
            }
            catch (Exception ex)
            {
                lock (retryGate)
                {
                    if (pendingContent == dataPackage) pendingContent = null;
                    retrying = false;
                }
                retryTimer?.Stop();
                Core.Utilities.Log.Debug($"clipboard: the copy could not be written ({ex.HResult:x8}): {ex.Message}");
            }
        }

        public void SetTextAndHtml(string text, string html)
        {
            var dataPackage = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
            if (!string.IsNullOrEmpty(text)) dataPackage.SetText(text);
            // CF_HTML: the fragment with the header Word and the browsers read it by, written as the browsers write it
            // (ClipboardHtml.Format: HtmlFormatHelper's upper-case one some editors pasted the header of as text).
            if (!string.IsNullOrEmpty(html)) dataPackage.SetHtmlFormat(Core.Utilities.ClipboardHtml.Format(html));
            SetContent(dataPackage);
        }

        // A bitmap (Windows offers it as CF_BITMAP, CF_DIB and CF_DIBV5: what Word, OneNote, mail and the chat programs
        // paste as a picture) and the text (what Notepad and code editors paste), in one item. No HTML with the picture in
        // it: a program that takes HTML before a bitmap (Word does) would then depend on reading a data: picture there.
        // Flushed, so the picture stays on the clipboard when the app is closed and another program asking for it does
        // not wait on this window to draw it.
        public async Task SetTextAndPngAsync(string text, byte[] png, string mathml = null)
        {
            var dataPackage = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
            if (!string.IsNullOrEmpty(text)) dataPackage.SetText(text);
            if (png is { Length: > 0 })
            {
                var stream = await StreamOf(png);
                dataPackage.SetBitmap(global::Windows.Storage.Streams.RandomAccessStreamReference.CreateFromStream(stream));
                // Word takes plain text over a bitmap, and pasted the diagram's code; it takes RTF over both, so the picture
                // goes in as RTF too (Word, WordPad, Outlook). Places with text only still get the code.
                var rtf = PngRtf(png);
                if (rtf != null) dataPackage.SetRtf(rtf);
            }
            // A formula's MathML in the shape Word writes it itself, under its two names: Word makes an equation of it, an
            // editable one; the rest keep the picture or the text. KaTeX's MathML as it was (no prefix, a <semantics> with
            // the TeX, ended by a zero byte, also as application/mathml+xml) Word did not take and pasted the picture
            // (2026-10-28, CB01's probe: Word's own bytes put back made an equation, ours under each name did not).
            var wordMathML = WordMathML(mathml);
            if (wordMathML != null)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(wordMathML);
                foreach (var format in MathMLFormats) dataPackage.SetData(format, await StreamOf(bytes));
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

        /// <summary>The clipboard formats a formula's MathML goes under (SetTextAndPngAsync): the two Word puts an equation under.</summary>
        internal static readonly string[] MathMLFormats = { "MathML", "MathML Presentation" };

        private static readonly System.Xml.Linq.XNamespace MathMLNs = "http://www.w3.org/1998/Math/MathML";

        /// <summary>
        /// MathML as Word writes it on the clipboard for an equation: an XML declaration, every element in the mml: prefix
        /// (with Office's m: namespace declared too), the root without attributes, no &lt;semantics&gt; wrapper and no
        /// annotations; no zero byte at the end. Null when it is empty or not MathML.
        /// </summary>
        internal static string WordMathML(string mathml)
        {
            if (string.IsNullOrWhiteSpace(mathml)) return null;
            System.Xml.Linq.XElement math;
            try { math = System.Xml.Linq.XElement.Parse(mathml); }
            catch (System.Xml.XmlException) { return null; }
            if (math.Name != MathMLNs + "math") return null;
            // <semantics>: its first child is the formula, the rest its annotations (KaTeX's TeX).
            var content = math.Elements().ToList();
            if (content.Count == 1 && content[0].Name == MathMLNs + "semantics")
                content = content[0].Elements().Take(1).ToList();
            content.RemoveAll(e => e.Name == MathMLNs + "annotation" || e.Name == MathMLNs + "annotation-xml");
            var root = new System.Xml.Linq.XElement(MathMLNs + "math",
                new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns + "mml", MathMLNs.NamespaceName),
                new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns + "m", "http://schemas.openxmlformats.org/officeDocument/2006/math"),
                content.Select(Unannotated));
            return "<?xml version=\"1.0\"?>\r\n" + root.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        }

        // A copy of the element without annotations below it, in the MathML namespace (prefixed by the root's mml:).
        private static System.Xml.Linq.XElement Unannotated(System.Xml.Linq.XElement element)
        {
            var copy = new System.Xml.Linq.XElement(element.Name, element.Attributes().Where(a => !a.IsNamespaceDeclaration));
            foreach (var node in element.Nodes())
            {
                if (node is System.Xml.Linq.XElement child)
                {
                    if (child.Name != MathMLNs + "annotation" && child.Name != MathMLNs + "annotation-xml") copy.Add(Unannotated(child));
                }
                else if (node is System.Xml.Linq.XText text) copy.Add(new System.Xml.Linq.XText(text.Value));
            }
            return copy;
        }

        private static async Task<global::Windows.Storage.Streams.InMemoryRandomAccessStream> StreamOf(byte[] bytes)
        {
            var stream = new global::Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var writer = new global::Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            return stream;
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