using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Typedown.Core.Utilities;

namespace Typedown.ReliabilityTests;

/// <summary>
/// A file's encoding found from its bytes (TextFileFormat.Read): UTF-8, GBK on a Western Windows, the system's code page
/// for an accent; and bytes that read in no encoding marked lossy - auto-save does not write over them - on a Western
/// (1252) and a Chinese (936) Windows alike. 1252 maps its undefined bytes to C1 controls: taken, no file was ever lossy
/// on a Western Windows (SB03 found it).
/// </summary>
[TestClass]
public class TextFileFormatTests
{
    private static byte[] Bytes(string ascii, params byte[] high) => Encoding.ASCII.GetBytes(ascii).Concat(high).Concat(Encoding.ASCII.GetBytes(" here.\n")).ToArray();

    [TestMethod]
    public void Utf8IsUtf8()
    {
        var (text, format) = TextFileFormat.Read(Encoding.UTF8.GetBytes("# 标题\n\nText.\n"), ansiCodePage: 1252);
        Assert.AreEqual(("# 标题\n\nText.\n", 65001, false), (text, format.Encoding.CodePage, format.LossyDecode));
    }

    [TestMethod]
    public void GbkOnAWesternWindowsIsGbk()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var (text, format) = TextFileFormat.Read(Encoding.GetEncoding("gb18030").GetBytes("笔记\n"), ansiCodePage: 1252);
        Assert.AreEqual(("笔记\n", false), (text, format.LossyDecode));
    }

    [TestMethod]
    public void AnAccentOnAWesternWindowsIs1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var (text, format) = TextFileFormat.Read(Bytes("Caf", 0xE9), ansiCodePage: 1252);
        Assert.AreEqual(("Café here.\n", false), (text, format.LossyDecode));
    }

    [TestMethod]
    public void AWesternFileOnAChineseWindowsIs1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var written = "Größe und Café: déjà vu, Übersicht.\n";
        var (text, format) = TextFileFormat.Read(Encoding.GetEncoding(1252).GetBytes(written), ansiCodePage: 936);
        Assert.AreEqual((written, 1252, false), (text, format.Encoding.CodePage, format.LossyDecode));
    }

    [TestMethod]
    public void BytesInNoEncodingAreLossyOnAWesternAndAChineseWindows()
    {
        // 0x81 0x20: a GBK lead byte before a space (no GBK character); 0x81 and 0x9D: undefined in 1252.
        var bytes = Bytes("# SB03\n\nSome text ", 0x81, 0x20, 0x9D, 0x81);
        foreach (var page in new[] { 1252, 936 })
            Assert.IsTrue(TextFileFormat.Read(bytes, ansiCodePage: page).Format.LossyDecode, $"code page {page}");
    }

    [TestMethod]
    public void OneByteUndefinedIn1252IsLossyThere()
    {
        Assert.IsTrue(TextFileFormat.Read(Bytes("Some text ", 0x81), ansiCodePage: 1252).Format.LossyDecode, "0x81 is no character in 1252: its C1 control is not text");
    }

    // R07: a save by hand over a file not read exactly asks first; one read exactly, a new file, or one gone from disk does not.
    [TestMethod]
    public void ASaveOverAFileNotReadExactlyAsksFirst()
    {
        var lossy = TextFileFormat.Read(Bytes("# SB03\n\nSome text ", 0x81, 0x20, 0x9D, 0x81), ansiCodePage: 1252).Format;
        Assert.IsTrue(lossy.NeedsConsentToOverwrite(fileExists: true), "the file's bytes would be replaced");
        Assert.IsFalse(lossy.NeedsConsentToOverwrite(fileExists: false), "no file left to lose");
        Assert.IsTrue(lossy.WithLineEnding("\r\n").NeedsConsentToOverwrite(fileExists: true), "another line ending is still not the bytes");
        var exact = TextFileFormat.Read(Encoding.UTF8.GetBytes("# Notes\n"), ansiCodePage: 1252).Format;
        Assert.IsFalse(exact.NeedsConsentToOverwrite(fileExists: true), "read exactly");
        Assert.IsFalse(TextFileFormat.Default.NeedsConsentToOverwrite(fileExists: true), "a new document");
    }

    // "Save as UTF-8" (the dialog's yes, or the status bar's Convert to UTF-8): the text as shown, the replacement
    // characters and all, as UTF-8 with no mark and the file's line ending; nothing asked again after it.
    [TestMethod]
    public void SavedAsUtf8ItIsTheTextAsShown()
    {
        var bytes = Bytes("# SB03\r\n\r\nSome text ", 0x81, 0x20, 0x9D, 0x81);
        var (text, format) = TextFileFormat.Read(bytes, ansiCodePage: 1252);
        var utf8 = format.AsUtf8();
        Assert.IsFalse(utf8.NeedsConsentToOverwrite(fileExists: true));
        var written = utf8.GetBytes(text + "Q");
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        Assert.AreEqual(text.Replace("\n", "\r\n") + "Q", strict.GetString(written), "valid UTF-8, the text as shown, CRLF kept");
        Assert.IsTrue(text.Contains('�'), "the undecodable bytes were shown as U+FFFD");
        Assert.AreNotEqual((byte)0xEF, written[0], "no byte order mark");
    }
}
