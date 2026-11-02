using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

internal static partial class Program
{
    /// <summary>
    /// PD01: File > Export > PDF in the app itself writes a PDF with the document's outline as its bookmarks and its
    /// picture in it. The engine and its parameters were checked in a browser (Tools/EditorBench/export-pdf-check.js);
    /// the file the app writes had not been looked at (docs/todo.md, 反馈补充: "应用端最终字节未自证").
    /// </summary>
    private static async Task PD01(List<string> notes)
    {
        using var c = await Session("e2e PD01");
        var folder = FixtureFolder("pd01");
        // A small PNG (1x1, red) beside the document.
        File.WriteAllBytes(Path.Combine(folder, "pd01.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg=="));
        var path = Path.Combine(folder, "pd01.md");
        File.WriteAllText(path, "# PD01 Report\n\nIntro text.\n\n## First part\n\nSome text.\n\n![red](pd01.png)\n\n## Second part\n\nMore text.\n\n### A detail\n\nEnd.\n");
        var pdf = Path.Combine(folder, "pd01.pdf");
        if (File.Exists(pdf)) File.Delete(pdf);
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        try
        {
            await Task.Delay(1500);
            await c.Call("test.export.run", new { windowId, type = "pdf", path = pdf });
            Check(await Eventually(() => Task.FromResult(File.Exists(pdf) && new FileInfo(pdf).Length > 1000), 60000, 300), "the PDF is written");
            await Task.Delay(1000);
            var bytes = File.ReadAllBytes(pdf);
            // The PDF's own structure, read as Latin-1: the outline (bookmarks) and the images are named in plain text.
            var text = Encoding.Latin1.GetString(bytes);
            var titles = Regex.Matches(text, @"/Title\s*[(<]").Count;
            var outlines = text.Contains("/Outlines");
            var images = Regex.Matches(text, @"/Subtype\s*/Image").Count;
            notes.Add($"PDF {bytes.Length} bytes: {(outlines ? "an outline" : "no outline")}, {titles} titles, {images} images");
            Check(text.StartsWith("%PDF-"), "the file is a PDF");
            Check(outlines && titles >= 4, $"its bookmarks are the document's headings ({titles} titles for 4 headings)");
            Check(images >= 1, "the picture is in it");
        }
        finally
        {
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
