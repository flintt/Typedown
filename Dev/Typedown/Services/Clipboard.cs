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
        /// The clipboard written once. Another program holding it (CLIPBRD_E_CANT_OPEN - 0x800401D0 "OpenClipboard 失败": a
        /// clipboard manager, a remote desktop session reading it) used to end Copy and Cut in an error dialog over the
        /// document (the user, 2026-11-22). The window's thread is not held while it is tried: the first write refused, the
        /// copy is kept and looked at again by the window's timer - 20 ms later, then every 120 ms for five seconds - and a
        /// copy that never lands is said in the window's notice, not in a dialog.
        /// </summary>
        private void SetContent(DataPackage dataPackage)
        {
            try
            {
                global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
                ClearPending();
            }
            catch (Exception ex)
            {
                if (!HeldByAnotherProgram(ex))
                {
                    // Not the clipboard's being held (the data itself refused, say): said in the log, as before.
                    Core.Utilities.Log.Debug($"clipboard: not written ({ex.HResult:x8}): {ex.Message}");
                    return;
                }
                KeepForASecondTry(dataPackage, ex);
            }
        }

        /// <summary>How long a write held by another program is tried again in the background.</summary>
        private static readonly TimeSpan RetryFor = TimeSpan.FromSeconds(5);

        /// <summary>The first look, a moment after the write was refused (a hold that is over in a moment), then every 120 ms.</summary>
        private static readonly TimeSpan FirstLook = TimeSpan.FromMilliseconds(20);
        private static readonly TimeSpan RetryEvery = TimeSpan.FromMilliseconds(120);

        /// <summary>No beat for this long: the timer's window is gone, and the next copy takes the retry over.</summary>
        private static readonly TimeSpan StaleAfter = TimeSpan.FromMilliseconds(400);

        /// <summary>HRESULT of CLIPBRD_E_CANT_OPEN ("OpenClipboard 失败"): the clipboard is open in another program.</summary>
        private const int ClipbrdCannotOpen = unchecked((int)0x800401D0);

        private static bool HeldByAnotherProgram(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
                if (e.HResult == ClipbrdCannotOpen) return true;
            return false;
        }

        // The write not made: the last one only (a later copy that lands takes its place, from any window - the clipboard is
        // one), the moment it stops being tried, and the window whose copy it was. The timer belongs to the window that
        // started it; one closed before the five seconds are over takes its timer with it, and the next copy finds no beat
        // and takes the retry over.
        private static readonly object retryGate = new();
        private static DataPackage pendingContent;
        private static DateTime pendingUntil;
        private static DateTime lastBeat;
        private static bool retrying;
        private static Clipboard pendingOwner;
        private DispatcherQueue dispatcher;
        private DispatcherQueueTimer retryTimer;
        private bool firstLook;

        /// <summary>
        /// A copy given up on: the clipboard stayed open in another program for the whole retry, so nothing was written
        /// (Copy or Cut has done nothing, and the reader is told without a dialog over the document).
        /// </summary>
        public event EventHandler WriteFailed;

        private void KeepForASecondTry(DataPackage dataPackage, Exception error)
        {
            var now = DateTime.UtcNow;
            lock (retryGate)
            {
                pendingContent = dataPackage;
                pendingOwner = this;
                pendingUntil = now.Add(RetryFor);
                // Another window's timer beat a moment ago: it writes this copy, the one kept now.
                if (retrying && now - lastBeat < StaleAfter) return;
                retrying = true;
                lastBeat = now;
            }
            Core.Utilities.Log.Debug($"clipboard: held by another program ({error.HResult:x8}); tried again in the background for {RetryFor.TotalSeconds:0} s");
            var queue = dispatcher ??= DispatcherQueue.GetForCurrentThread();
            if (queue == null)
            {
                // No window thread to go back to (a call from nowhere the window could be): the copy is not made.
                DropPending(dataPackage, failed: false);
                return;
            }
            if (retryTimer == null)
            {
                retryTimer = queue.CreateTimer();
                retryTimer.IsRepeating = true;
                retryTimer.TickSafe((_, _) => RetryWrite());
            }
            firstLook = true;
            retryTimer.Interval = FirstLook;
            retryTimer.Start();
        }

        // A copy written (or replaced by another): nothing left to retry.
        private void ClearPending()
        {
            lock (retryGate)
            {
                pendingContent = null;
                pendingOwner = null;
                retrying = false;
            }
            retryTimer?.Stop();
        }

        // The copy of one package no longer pending (written, or given up on); the window that made it, to tell if it failed.
        private static Clipboard DropPending(DataPackage only, bool failed)
        {
            Clipboard owner = null;
            lock (retryGate)
            {
                if (pendingContent != only) return null;
                owner = failed ? pendingOwner : null;
                pendingContent = null;
                pendingOwner = null;
                retrying = false;
            }
            return owner;
        }

        private static void TellWriteFailed(Clipboard owner)
        {
            if (owner == null) return;
            try
            {
                owner.WriteFailed?.Invoke(owner, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Core.Utilities.Log.Debug($"clipboard: the notice for a copy not made failed: {ex.Message}");
            }
        }

        private void RetryWrite()
        {
            DataPackage dataPackage;
            lock (retryGate)
            {
                lastBeat = DateTime.UtcNow;
                dataPackage = pendingContent;
            }
            if (dataPackage == null)
            {
                retryTimer?.Stop();
                return;
            }
            if (DateTime.UtcNow > pendingUntil)
            {
                var owner = DropPending(dataPackage, failed: true);
                retryTimer?.Stop();
                Core.Utilities.Log.Debug("clipboard: still held by another program; the copy was not made");
                TellWriteFailed(owner);
                return;
            }
            try
            {
                global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
                DropPending(dataPackage, failed: false);
                retryTimer?.Stop();
                Core.Utilities.Log.Debug("clipboard: the copy written once the other program let the clipboard go");
            }
            catch (Exception ex) when (HeldByAnotherProgram(ex))
            {
                // Still held: looked at again on the next tick; the first tick's short wait gives way to the usual one.
                if (firstLook) { firstLook = false; retryTimer.Interval = RetryEvery; }
            }
            catch (Exception ex)
            {
                DropPending(dataPackage, failed: false);
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