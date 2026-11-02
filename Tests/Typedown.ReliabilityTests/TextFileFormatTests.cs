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
}
