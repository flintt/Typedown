using System;
using System.Linq;
using Typedown.Core.Pages.SettingPages;

namespace Typedown.Core.Pages
{
    public static class Route
    {
        public static Type GetRootPageType(string name) => name switch
        {
            "Main" => typeof(MainPage),
            "Settings" => typeof(SettingsPage),
            _ => null
        };

        public static Type GetSettingsPageType(string name) => name switch
        {
            "About" => typeof(AboutPage),
            "Editor" => typeof(EditorPage),
            "ExportConfig" => typeof(ExportConfigPage),
            "Export" => typeof(ExportPage),
            "General" => typeof(GeneralPage),
            "Image" => typeof(ImagePage),
            "ImageUpload" => typeof(ImageUploadPage),
            "Shortcut" => typeof(ShortcutPage),
            "UploadConfig" => typeof(UploadConfigPage),
            "View" => typeof(ViewPage),
            _ => null
        };

        /// <summary>The name a settings section is reached by (the reverse of <see cref="GetSettingsPageType"/>), or null.</summary>
        public static string GetSettingsPageName(Type type) =>
            new[] { "About", "Editor", "ExportConfig", "Export", "General", "Image", "ImageUpload", "Shortcut", "UploadConfig", "View" }
                .FirstOrDefault(name => GetSettingsPageType(name) == type);
    }
}
