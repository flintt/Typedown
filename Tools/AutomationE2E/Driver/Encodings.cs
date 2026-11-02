using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

internal static partial class Program
{
    /// <summary>
    /// EN01: files with no byte order mark in the encodings readers have (a reader's request, 2026-11-02: GBK, Big5,
    /// Shift-JIS... came up garbled, read as UTF-8), opened in the app: each shows its text as written and the status
    /// bar names its encoding; a key typed and the file saved, it is still in that encoding - decoded with it, the file
    /// is the document's text, every character of the language there. TextFileFormatTests covers the guessing itself.
    /// </summary>
    private static async Task EN01(List<string> notes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var files = new (string Name, string Encoding, string Label, string Text)[]
        {
            ("zh-hans-gb18030", "gb18030", "GB18030", "# 中文标题\n\n这是一段中文文本，用来测试编码识别是否正确。我们在这里写一些常见的汉字：天气、学习、工作、朋友、电脑、软件。\n\n- 列表项一\n- 列表项二\n"),
            ("zh-hant-big5", "big5", "BIG5", "# 繁體標題\n\n這是一段繁體中文文本，用來測試編碼識別是否正確。我們在這裡寫一些常見的漢字：天氣、學習、工作、朋友、電腦、軟體。\n"),
            ("ja-shift-jis", "shift_jis", "SHIFT_JIS", "# 見出し\n\nこれは文字コードの判定をテストするための日本語の文章です。ひらがな、カタカナ、漢字が混ざっています。東京、大阪、京都。\n"),
            ("ja-euc-jp", "euc-jp", "EUC-JP", "# 見出し\n\nこれは文字コードの判定をテストするための日本語の文章です。ひらがな、カタカナ、漢字が混ざっています。東京、大阪、京都。\n"),
            ("ko-euc-kr", "euc-kr", "EUC-KR", "# 제목\n\n이것은 인코딩 감지를 테스트하기 위한 한국어 문장입니다. 서울, 부산, 대구. 컴퓨터와 소프트웨어.\n"),
            ("fr-de-windows-1252", "windows-1252", "WINDOWS-1252", "# Titre\n\nCeci est un texte français avec des accents : élève, à côté, garçon, Noël. Und ein deutscher Satz: Größe, Übung, schön, Mädchen.\n"),
            ("ru-windows-1251", "windows-1251", "WINDOWS-1251", "# Заголовок\n\nЭто русский текст для проверки определения кодировки. Москва, книга, компьютер, программа.\n"),
            ("mixed-utf-16le", "utf-16le-nobom", "UTF-16 LE", "# Notes\n\nSome text, 一些中文, 日本語, 한국어 and more text.\n"),
            ("short-zh-gbk", "gb18030", "GB18030", "# 笔记\n\n今天\n"),
        };
        using var c = await Session("e2e EN01");
        var failures = new List<string>();
        string? windowId = null;
        IntPtr window = IntPtr.Zero;
        var hidden = (string?)null;
        foreach (var (name, encodingName, label, text) in files)
        {
            var encoding = encodingName == "utf-16le-nobom" ? new UnicodeEncoding(false, false) : Encoding.GetEncoding(encodingName);
            var path = Path.Combine(fixtures, $"en01-{name}.md");
            File.WriteAllBytes(path, encoding.GetBytes(text));
            var id = await Open(c, path);
            try
            {
                if (windowId == null)
                {
                    windowId = await WindowIdOf(c, id);
                    window = await WindowOf(windowId, c);
                    hidden = (string?)(await c.Call("test.settings.get", new { windowId, name = "StatusBarHiddenItems" }))["value"];
                    await c.Call("window.setView", new { windowId, statusBar = true, mode = "visual" });
                    await c.Call("test.settings.set", new { windowId, name = "StatusBarHiddenItems", value = "" });
                }
                await Task.Delay(1200);
                var shown = (string?)(await Get(c, id))["text"] ?? "";
                var bar = BarText(window, "StatusBarFileFormat");
                var ok = shown == text && bar.Contains(label);
                notes.Add($"{name}: status bar '{bar}', text {(shown == text ? "as written" : "DIFFERENT: " + shown.Replace("\n", "\\n")[..Math.Min(60, shown.Length)])}");
                if (!ok) { failures.Add($"{name}: {(shown == text ? "" : "the text is not as written; ")}{(bar.Contains(label) ? "" : $"the status bar says '{bar}', not {label}")}"); continue; }
                // A key and a save: the file written back in its own encoding.
                await TypeIntoWindow(c, windowId!, "X");
                string edited = text;
                for (var i = 0; i < 50 && edited == text; i++) { await Task.Delay(200); edited = (string?)(await Get(c, id))["text"] ?? ""; }
                await c.Call("document.save", new { documentId = id });
                await Task.Delay(500);
                var bytes = File.ReadAllBytes(path);
                var decoded = encoding.GetString(bytes).Replace("\r\n", "\n");
                var language = new string(text.Where(ch => ch > 127).Distinct().ToArray());
                var kept = decoded == edited && encoding.GetBytes(encoding.GetString(bytes)).SequenceEqual(bytes) && language.All(decoded.Contains) && decoded.Contains('X');
                notes.Add($"{name}: saved {bytes.Length} bytes, {(kept ? "in " + label + ", every character there" : "NOT the document: " + decoded.Replace("\n", "\\n")[..Math.Min(60, decoded.Length)])}");
                if (!kept) failures.Add($"{name}: the saved file is not the document's text in {label}");
            }
            finally
            {
                try { await c.Call("document.close", new { documentId = id }); } catch { }
            }
        }
        if (windowId != null) try { await c.Call("test.settings.set", new { windowId, name = "StatusBarHiddenItems", value = hidden ?? "" }); } catch { }
        Check(failures.Count == 0, string.Join("; ", failures));
    }
}
