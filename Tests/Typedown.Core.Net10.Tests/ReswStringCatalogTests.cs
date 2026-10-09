using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Typedown.Contracts.Localization;
using Typedown.Core.Localization;

namespace Typedown.Core.Net10.Tests;

[TestClass]
public sealed class ReswStringCatalogTests
{
    private string root = null!;

    [TestInitialize]
    public void Initialize()
    {
        root = Path.Combine(Path.GetTempPath(), "typedown-resw-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        Write("en", LocalizationResourceSource.CommonResources,
            ("File", "File"), ("Settings.Title", "Settings"));
        Write("en", LocalizationResourceSource.DialogResources,
            ("File", "Dialog file"), ("Ok", "OK"));
        Write("zh-Hans", LocalizationResourceSource.CommonResources,
            ("File", "文件"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.Delete(root, recursive: true);
    }

    [TestMethod]
    public void All_sources_use_the_existing_precedence_and_dotted_keys_verbatim()
    {
        var catalog = new ReswStringCatalog(root);

        Assert.AreEqual("File", catalog.GetString("File", "en"));
        Assert.AreEqual(
            "Dialog file",
            catalog.GetString("File", "en", LocalizationResourceSource.DialogResources));
        Assert.AreEqual("Settings", catalog.GetString("Settings.Title", "en"));
    }

    [TestMethod]
    public void Language_resolution_maps_Chinese_regions_and_falls_back_to_English()
    {
        var catalog = new ReswStringCatalog(root);

        Assert.AreEqual("zh-Hans", catalog.ResolveLanguage("default", "zh-CN"));
        Assert.AreEqual("文件", catalog.GetString("File", "zh-CN"));
        Assert.AreEqual("en", catalog.ResolveLanguage("fr-CA", "de-DE"));
        Assert.AreEqual("OK", catalog.GetString("Ok", "fr-CA"));
    }

    [TestMethod]
    public void Missing_or_malformed_resources_do_not_break_the_shell()
    {
        File.WriteAllText(
            Path.Combine(root, "en", "SettingsResources.resw"),
            "<root><data",
            Encoding.UTF8);
        var catalog = new ReswStringCatalog(root);

        Assert.AreEqual("Unknown.Key", catalog.GetString("Unknown.Key", "en"));
        Assert.AreEqual(
            "Broken.Key",
            catalog.GetString(
                "Broken.Key",
                "en",
                LocalizationResourceSource.SettingsResources));
    }

    private void Write(
        string language,
        LocalizationResourceSource source,
        params (string Key, string Value)[] values)
    {
        var directory = Path.Combine(root, language);
        Directory.CreateDirectory(directory);
        var data = string.Join(
            Environment.NewLine,
            values.Select(value =>
                $"  <data name=\"{value.Key}\" xml:space=\"preserve\"><value>{value.Value}</value></data>"));
        File.WriteAllText(
            Path.Combine(directory, $"{source}.resw"),
            $"<?xml version=\"1.0\" encoding=\"utf-8\"?><root>{Environment.NewLine}{data}{Environment.NewLine}</root>",
            Encoding.UTF8);
    }
}
