using System.Linq;
using System.Text;
using Typedown.Core.Utilities;
using Xunit;

// A text file's encoding found from its bytes, and the file written back byte for byte. Files with no byte order mark
// were all read as UTF-8: a GBK, Big5 or Shift-JIS file came up garbled (a reader's report, 2026-11-02).
public class TextFileFormatTests
{
    static TextFileFormatTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    // A paragraph or so of each language, with Markdown in it, as a reader's notes would be.
    public static TheoryData<string, string, string> Documents => new()
    {
        { "gb18030", "GB18030", "# 标题\n\n这是一段中文文本，用来测试编码识别是否正确。我们在这里写一些常见的汉字：天气、学习、工作、朋友、电脑、软件。\n\n- 列表项一\n- 列表项二\n\n```js\nconst 名字 = '值'\n```\n" },
        { "big5", "BIG5", "# 標題\n\n這是一段繁體中文文本，用來測試編碼識別是否正確。我們在這裡寫一些常見的漢字：天氣、學習、工作、朋友、電腦、軟體。\n\n- 清單項目一\n- 清單項目二\n" },
        { "shift_jis", "SHIFT_JIS", "# 見出し\n\nこれは文字コードの判定をテストするための日本語の文章です。ひらがな、カタカナ、漢字が混ざっています。東京、大阪、京都。\n\n- 項目一\n- 項目二\n" },
        { "euc-jp", "EUC-JP", "# 見出し\n\nこれは文字コードの判定をテストするための日本語の文章です。ひらがな、カタカナ、漢字が混ざっています。東京、大阪、京都。\n" },
        { "euc-kr", "EUC-KR", "# 제목\n\n이것은 인코딩 감지를 테스트하기 위한 한국어 문장입니다. 서울, 부산, 대구. 컴퓨터와 소프트웨어.\n\n- 항목 하나\n- 항목 둘\n" },
        { "windows-1252", "WINDOWS-1252", "# Titre\n\nCeci est un texte français avec des accents : élève, à côté, garçon, Noël. Und ein deutscher Satz: Größe, Übung, schön, Mädchen.\n" },
        { "windows-1251", "WINDOWS-1251", "# Заголовок\n\nЭто русский текст для проверки определения кодировки. Москва, книга, компьютер, программа.\n" },
    };

    [Theory]
    [MemberData(nameof(Documents))]
    public void A_document_in_a_legacy_encoding_is_read_as_written_and_saved_byte_for_byte(string encoding, string label, string text)
    {
        // On an English system, so the system's code page cannot be what found it.
        foreach (var lineEnding in new[] { "\n", "\r\n" })
        {
            var bytes = Encoding.GetEncoding(encoding).GetBytes(text.Replace("\n", lineEnding));
            var (read, format) = TextFileFormat.Read(bytes, ansiCodePage: 1252);
            Assert.Equal(text, read);
            Assert.Equal(label, format.EncodingLabel);
            Assert.False(format.LossyDecode);
            Assert.Equal(bytes, format.GetBytes(read));
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void The_same_documents_in_UTF_8_and_UTF_16_with_and_without_their_mark(string _, string __, string text)
    {
        foreach (var (encoding, label) in new (Encoding, string)[]
        {
            (new UTF8Encoding(false), "UTF-8"), (new UTF8Encoding(true), "UTF-8 BOM"),
            (new UnicodeEncoding(false, true), "UTF-16 LE"), (new UnicodeEncoding(true, true), "UTF-16 BE"),
        })
        {
            var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
            var (read, format) = TextFileFormat.Read(bytes, ansiCodePage: 936);
            Assert.Equal(text, read);
            Assert.Equal(label, format.EncodingLabel);
            Assert.Equal(bytes, format.GetBytes(read));
        }
    }

    [Theory]
    [InlineData(false, "UTF-16 LE")]
    [InlineData(true, "UTF-16 BE")]
    public void UTF_16_without_its_mark_is_told_by_its_zero_bytes(bool bigEndian, string label)
    {
        var text = "# Notes\n\nSome text, 一些中文, and more text.\n";
        var bytes = new UnicodeEncoding(bigEndian, false).GetBytes(text);
        var (read, format) = TextFileFormat.Read(bytes, ansiCodePage: 936);
        Assert.Equal(text, read);
        Assert.Equal(label, format.EncodingLabel);
        Assert.False(format.HasByteOrderMark);
        Assert.Equal(bytes, format.GetBytes(read));
    }

    [Theory]
    [InlineData("中文")]
    [InlineData("你好，世界")]
    [InlineData("# 笔记\n\n今天\n")]
    public void A_few_Chinese_characters_on_Chinese_Windows_are_GBK(string text)
    {
        // Too short for the detector: the system's code page (936) decides.
        var bytes = Encoding.GetEncoding("gb18030").GetBytes(text);
        var (read, format) = TextFileFormat.Read(bytes, ansiCodePage: 936);
        Assert.Equal(text, read);
        Assert.Equal("GB18030", format.EncodingLabel);
        Assert.False(format.LossyDecode);
    }

    [Theory]
    [InlineData("中文")]
    [InlineData("# 笔记\n\n今天\n")]
    [InlineData("你好，世界！")]
    public void A_few_Chinese_characters_on_English_Windows_are_GBK_too(string text)
    {
        // The system's code page (1252) read "笔记" as "±Ê¼Ç" (EN01 on w10, an English Windows).
        var bytes = Encoding.GetEncoding("gb18030").GetBytes(text);
        var (read, format) = TextFileFormat.Read(bytes, ansiCodePage: 1252);
        Assert.Equal(text, read);
        Assert.Equal("GB18030", format.EncodingLabel);
    }

    [Theory]
    [InlineData("Größe")]
    [InlineData("Übung für Mädchen")]
    [InlineData("élève à côté")]
    public void A_few_accented_Western_words_are_not_taken_for_Chinese(string text)
    {
        var bytes = Encoding.GetEncoding(1252).GetBytes(text);
        var (read, format) = TextFileFormat.Read(bytes, ansiCodePage: 1252);
        Assert.Equal(text, read);
        Assert.NotEqual("GB18030", format.EncodingLabel);
    }

    [Fact]
    public void A_few_accented_letters_on_Western_Windows_are_Windows_1252()
    {
        var bytes = Encoding.GetEncoding(1252).GetBytes("café crème");
        var (read, format) = TextFileFormat.Read(bytes, ansiCodePage: 1252);
        Assert.Equal("café crème", read);
        Assert.False(format.LossyDecode);
        Assert.Equal(bytes, format.GetBytes(read));
    }

    [Fact]
    public void Plain_ASCII_and_an_empty_file_are_UTF_8()
    {
        foreach (var text in new[] { "", "# Title\n\nplain text\n" })
        {
            var (read, format) = TextFileFormat.Read(Encoding.ASCII.GetBytes(text), ansiCodePage: 936);
            Assert.Equal(text, read);
            Assert.Equal("UTF-8", format.EncodingLabel);
        }
    }

    [Fact]
    public void Bytes_no_encoding_reads_whole_are_UTF_8_marked_lossy_so_nothing_saves_over_them()
    {
        // Not text in any of them: lone bytes the system's code page (UTF-8 here) cannot read either.
        var bytes = new byte[] { 0x23, 0x20, 0xFF, 0xFE, 0x80, 0x81, 0xC0, 0x0A, 0xF5, 0x90 };
        var (_, format) = TextFileFormat.Read(bytes, ansiCodePage: 65001);
        Assert.Equal("UTF-8", format.EncodingLabel);
        Assert.True(format.LossyDecode);
    }

    [Fact]
    public void The_readers_choice_from_the_status_bar_wins_over_the_guess()
    {
        var text = "# 標題\n\n這是繁體中文。\n";
        var bytes = Encoding.GetEncoding("big5").GetBytes(text);
        var (read, format) = TextFileFormat.Read(bytes, TextFileFormat.EncodingByName("big5"), ansiCodePage: 936);
        Assert.Equal(text, read);
        Assert.Equal("BIG5", format.EncodingLabel);
    }

    [Fact]
    public void Every_encoding_offered_to_reopen_with_is_there()
    {
        foreach (var name in TextFileFormat.ReopenEncodings) Assert.NotNull(TextFileFormat.EncodingByName(name));
    }
}
