using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// IU05: pictures kept in the document itself (#52, Settings > Image > Embed in the document). A pasted screenshot goes in
/// as a PNG data address, or a JPEG one when chosen; a picture file inserted goes in with its own bytes. The picture's menu,
/// opened by a right-click on it, saves an embedded one into the image folder and points at the file, and embeds it again.
/// </summary>
internal static partial class Program
{
    private static async Task IU05(List<string> notes)
    {
        using var c = await Session("e2e IU05");
        var folder = Path.Combine(fixtures, "iu05");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        Directory.CreateDirectory(folder);
        var doc = Path.Combine(folder, "iu05.md");
        File.WriteAllText(doc, "# IU05\n");
        var id = await Open(c, doc);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        static string MakePng(string path, int width, int height)
        {
            using var bitmap = new System.Drawing.Bitmap(width, height);
            for (var x = 0; x < width; x++)
                for (var y = 0; y < height; y++)
                    bitmap.SetPixel(x, y, System.Drawing.Color.FromArgb(255, (x * 7) % 256, (y * 5) % 256, 120));
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            return path;
        }
        static byte[]? Decode(string address)
        {
            var m = Regex.Match(address, @"^data:image/[\w+.-]+;base64,([A-Za-z0-9+/=]+)$");
            return m.Success ? Convert.FromBase64String(m.Groups[1].Value) : null;
        }
        async Task<string> Text() => (string)(await Get(c, id))["text"]!;
        try
        {
            await c.Call("window.setView", new { windowId, mode = "visual", bounds = new { x = 40, y = 40, width = 1200, height = 800 } });
            var shot = MakePng(Path.Combine(folder, "shot.png"), 40, 30);

            // A screenshot pasted: a PNG data address, the picture itself in it.
            await c.Call("test.settings.set", new { windowId, name = "InsertClipboardImageAction", value = "Base64" });
            var png = (string)(await c.Call("test.images.paste", new { windowId, path = shot }))["address"]!;
            notes.Add("pasted: " + png.Substring(0, Math.Min(60, png.Length)) + $"... ({png.Length} chars)");
            var pngBytes = Decode(png);
            Check(png.StartsWith("data:image/png;base64,") && pngBytes != null && pngBytes.Take(4).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }), "a pasted screenshot is embedded as a PNG");
            using (var back = new System.Drawing.Bitmap(new MemoryStream(pngBytes!)))
                Check(back.Width == 40 && back.Height == 30, $"the same picture ({back.Width}x{back.Height})");

            // JPEG chosen for pasted pictures.
            await c.Call("test.settings.set", new { windowId, name = "Base64ClipboardJpeg", value = true });
            var jpeg = (string)(await c.Call("test.images.paste", new { windowId, path = shot }))["address"]!;
            var jpegBytes = Decode(jpeg);
            Check(jpeg.StartsWith("data:image/jpeg;base64,") && jpegBytes != null && jpegBytes[0] == 0xFF && jpegBytes[1] == 0xD8, "with JPEG chosen, a JPEG");

            // A picture file inserted: its own bytes in the document.
            await c.Call("test.settings.set", new { windowId, name = "InsertLocalImageAction", value = "Base64" });
            var file = MakePng(Path.Combine(folder, "file.png"), 240, 160);
            await c.Call("test.images.insert", new { windowId, paths = new[] { file } });
            string text = "";
            var embedded = await Eventually(async () => (text = await Text()).Contains("data:image/png;base64,"), 5000, 200);
            var inDoc = Regex.Match(text, @"\]\((data:image/png;base64,[A-Za-z0-9+/=]+)\)").Groups[1].Value;
            notes.Add($"document: {text.Length} chars; embedded {inDoc.Length} chars");
            Check(embedded && Decode(inDoc)?.SequenceEqual(File.ReadAllBytes(file)) == true, "an inserted picture file is in the document with its own bytes");

            // Its menu, by a right-click on the picture drawn: saved into the image folder, the document points at the file.
            async Task RightClickPicture()
            {
                var at = (await c.Call("test.editor.eval", new { windowId, script = "(() => { const i = document.querySelector('#ag-editor-id img'); if (!i) return null; i.scrollIntoView({ block: 'center' }); const r = i.getBoundingClientRect(); return { x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) } })()" }))["result"]!;
                Check(at.Type == JTokenType.Object, "the picture is drawn in the editor");
                var screen = await c.Call("test.editor.screenPoint", new { windowId, x = (int)at["x"]!, y = (int)at["y"]! });
                GetWindowRect(window, out var rc);
                // Chosen first (a click on it), as a person does: the menu has the picture's entries for the picture chosen.
                await Click(window, (int)screen["x"]! - rc.Left, (int)screen["y"]! - rc.Top);
                await Task.Delay(400);
                await Click(window, (int)screen["x"]! - rc.Left, (int)screen["y"]! - rc.Top, Mouse.Right);
                notes.Add("menu after the right-click: " + Screenshot("IU05-menu"));
            }
            await RightClickPicture();
            System.Windows.Automation.AutomationElement? extract = null;
            Check(await Eventually(() => Task.FromResult((extract = FindAnywhereInHost(window, "ExtractImageItem")) != null), 4000, 150), "the picture's menu offers to save it into the image folder");
            Check(FindAnywhereInHost(window, "EmbedImageItem") == null, "and not to embed it (it is embedded)");
            await ClickInPopup(window, extract!);
            var linked = await Eventually(async () => !(text = await Text()).Contains("data:image"), 5000, 200);
            var address = Regex.Match(text, @"!\[[^\]]*\]\(([^)\s]+)").Groups[1].Value;
            var saved = Path.GetFullPath(Path.Combine(folder, Uri.UnescapeDataString(address)));
            notes.Add($"saved: {address} -> {saved}");
            Check(linked && !address.StartsWith("data:") && File.Exists(saved), "the document now points at a file");
            Check(File.Exists(saved) && File.ReadAllBytes(saved).SequenceEqual(File.ReadAllBytes(file)), "which is the picture, byte for byte");

            // And embedded again from the same menu.
            await Task.Delay(800);
            await RightClickPicture();
            System.Windows.Automation.AutomationElement? embed = null;
            Check(await Eventually(() => Task.FromResult((embed = FindAnywhereInHost(window, "EmbedImageItem")) != null), 4000, 150), "a file's menu offers to embed it");
            await ClickInPopup(window, embed!);
            var again = await Eventually(async () => (text = await Text()).Contains("data:image/png;base64,"), 5000, 200);
            Check(again && Decode(Regex.Match(text, @"\]\((data:image/png;base64,[A-Za-z0-9+/=]+)\)").Groups[1].Value)?.SequenceEqual(File.ReadAllBytes(file)) == true, "embedded again, the same bytes");

            // Source mode: the encoded part is one short label (its kind and size), the text itself whole.
            await c.Call("window.setView", new { windowId, mode = "source" });
            JToken fold = JValue.CreateNull();
            var folded = await Eventually(async () => (fold = (await c.Call("test.editor.eval", new { windowId, script = "(() => { const f = document.querySelector('.cm-data-uri-fold'); const l = f && f.closest('.CodeMirror-line'); return f ? { label: f.textContent, line: l ? l.textContent.length : -1 } : null })()" }))["result"]!).Type == JTokenType.Object, 5000, 200);
            notes.Add("source mode: " + fold.ToString(Formatting.None));
            Check(folded && ((string?)fold["label"] ?? "").Contains("PNG") && (int)fold["line"]! < 200, "in source mode the base64 is one short label");
            Check((await Text()).Contains(inDoc), "and the document's text is still the whole address");
            await c.Call("window.setView", new { windowId, mode = "visual" });
        }
        finally
        {
            await CloseMenus();
            foreach (var (name, value) in new (string, JToken)[] { ("InsertClipboardImageAction", "None"), ("InsertLocalImageAction", "None"), ("Base64ClipboardJpeg", false) })
            {
                try { await c.Call("test.settings.set", new { windowId, name, value }); } catch { }
            }
            try { await c.Call("document.save", new { documentId = id }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
