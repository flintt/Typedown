using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// IU06: a picture kept in the document (data:image/...;base64,...) is never shown as its encoding in the visual editor
/// (the user, 2026-11-13): in source mode with the caret on the line below the picture, switched to visual, the whole
/// base64 showed; and in the visual editor the caret moved up onto the picture showed its source too. Where the source
/// is shown (a raw HTML block's source: an <img src="data:..."> alone at the start of the document), the encoding is a
/// short label, as source mode folds it.
/// </summary>
internal static partial class Program
{
    private static async Task IU06(List<string> notes)
    {
        using var c = await Session("e2e IU06");
        var folder = Path.Combine(fixtures, "iu06");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        Directory.CreateDirectory(folder);
        // A screenshot's size: a few hundred KB of encoding.
        var shot = Path.Combine(folder, "shot.png");
        using (var bitmap = new System.Drawing.Bitmap(500, 320))
        {
            var random = new Random(6);
            for (var x = 0; x < bitmap.Width; x++)
                for (var y = 0; y < bitmap.Height; y++)
                    bitmap.SetPixel(x, y, System.Drawing.Color.FromArgb(255, random.Next(256), random.Next(256), (x + y) % 256));
            bitmap.Save(shot, System.Drawing.Imaging.ImageFormat.Png);
        }
        var address = "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(shot));
        // Markdown's own picture; an HTML <img> with the attributes another editor writes, between blank lines (a picture
        // in a paragraph); and the user's document as it was (2026-11-13), that <img> alone: a raw HTML block, whose
        // source shows while the caret is in it - where the whole base64 showed.
        foreach (var (name, text, below) in new[]
        {
            ("markdown", $"# IU06\n\n![]({address})\n\nNext line\n", 3),
            ("html", $"\r\n\r\n\r\n\r\n<img title=\"\" src=\"{address}\" alt=\"\" style=\"zoom:100%;\">\r\n\r\n\r\n\r\n\r\nNext line\r\n", 5),
            ("block", $"<img title=\"\" src=\"{address}\" alt=\"\" style=\"zoom:100%;\">\r\n", 1),
        })
        {
            var doc = Path.Combine(folder, $"iu06-{name}.md");
            File.WriteAllText(doc, text);
            await Run(name, doc, below);
        }

        async Task Run(string name, string doc, int below)
        {
            var id = await Open(c, doc);
            var windowId = await WindowIdOf(c, id);
            var window = await WindowOf(windowId, c);
            // What the reader sees of the picture's paragraph, and of the whole editor.
            async Task<JToken> Seen() => (await c.Call("test.editor.eval", new { windowId, script = "(() => { const e = document.getElementById('ag-editor-id'); const i = e && e.querySelector('.ag-inline-image'); const p = i && i.closest('p'); return { paragraph: p ? p.innerText.length : -1, sample: p ? p.innerText.slice(0, 60) : '', editor: e ? e.innerText.length : -1, classes: i ? i.className : '' } })()" }))["result"]!;
            void Shown(JToken seen, string when)
            {
                when = name + " " + when;
                notes.Add($"{when}: {seen.ToString(Formatting.None)} {Screenshot("IU06-" + when.Replace(' ', '-'))}");
                Check((int)seen["paragraph"]! < 200 && (int)seen["editor"]! < 400, $"{when}: the encoding is not shown ({(int)seen["editor"]!} characters in the editor)");
            }
            try
            {
                await c.Call("window.setView", new { windowId, mode = "visual", bounds = new { x = 40, y = 40, width = 1200, height = 800 } });
                foreach (var by in new[] { "setView", "shortcut" })
                {
                    // Source mode, the caret on the line below the picture (the blank line), then back to visual.
                    await c.Call("window.setView", new { windowId, mode = "source" });
                    await Task.Delay(600);
                    await Activate(window);
                    await c.Call("test.editor.eval", new { windowId, script = "(() => { const cm = document.querySelector('.CodeMirror').CodeMirror; cm.focus(); cm.setCursor({ line: " + below + ", ch: 0 }); return cm.getCursor().line })()" });
                    await Task.Delay(300);
                    if (by == "setView") await c.Call("window.setView", new { windowId, mode = "visual" });
                    else Chord("Ctrl+Slash");
                    await Task.Delay(1200);
                    Shown(await Seen(), $"{by} source to visual");

                    // The caret moved up onto the picture, from the line below it (the block: from where it is).
                    if (name != "block") await ClickEditorText(c, windowId, window, "Next line");
                    Chord("Home");
                    await Task.Delay(200);
                    Chord("Up");
                    await Task.Delay(500);
                    Shown(await Seen(), $"{by} up once");
                    Chord("Up");
                    await Task.Delay(500);
                    Shown(await Seen(), $"{by} up twice");
                }
            }
            finally
            {
                try { await c.Call("document.close", new { documentId = id }); } catch { }
            }
        }
    }
}
