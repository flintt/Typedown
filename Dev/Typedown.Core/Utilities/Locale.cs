using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.UI.Xaml.Markup;

namespace Typedown.Core.Utilities
{
    public static class Locale
    {
        public enum ResourceSource
        {
            All,
            CommonResources,
            DialogResources,
            SettingsResources,
            Resources
        }

        private static readonly string assemblyName = typeof(Locale).Assembly.GetName().Name;

        // The app's resources (MRT Core, which WinUI 3 uses in place of UWP's ResourceManager.Current): this library's .resw
        // files are merged into the app's resources.pri under its assembly name.
        private static readonly ResourceManager Manager = new();

        public static IReadOnlyDictionary<ResourceSource, ResourceMap> ResourcesDictionary = new Dictionary<ResourceSource, ResourceMap>()
        {
            {ResourceSource.CommonResources,  Manager.MainResourceMap.GetSubtree($"{assemblyName}/" + nameof(ResourceSource.CommonResources))},
            {ResourceSource.DialogResources,  Manager.MainResourceMap.GetSubtree($"{assemblyName}/" + nameof(ResourceSource.DialogResources))},
            {ResourceSource.SettingsResources,  Manager.MainResourceMap.GetSubtree($"{assemblyName}/" + nameof(ResourceSource.SettingsResources))},
            {ResourceSource.Resources,  Manager.MainResourceMap.GetSubtree($"{assemblyName}/" + nameof(ResourceSource.Resources))}
        };

        public static Dictionary<string, string> SupportedLangs { get; } = new()
        {
            {"af","Afrikaans"},
            {"am","አማርኛ"},
            {"ar","العربية"},
            {"as","অসমীয়া"},
            {"bg","Български"},
            {"bn","বাংলা"},
            {"bs","Bosnian"},
            {"ca","Català"},
            {"cs","Čeština"},
            {"cy","Cymraeg"},
            {"da","Dansk"},
            {"de","Deutsch"},
            {"el","Ελληνικά"},
            {"en","English"},
            {"es","Español"},
            {"et","Eesti"},
            {"eu","Euskara"},
            {"fa","فارسی"},
            {"fi","Suomi"},
            {"fil","Filipino"},
            {"fr","Français"},
            {"ga","Gaeilge"},
            {"gl","Galego"},
            {"gu","ગુજરાતી"},
            {"he","עברית"},
            {"hi","हिन्दी"},
            {"hr","Hrvatski"},
            {"hu","Magyar"},
            {"hy","Հայերեն"},
            {"id","Indonesia"},
            {"is","Íslenska"},
            {"it","Italiano"},
            {"ja","日本語"},
            {"ka","ქართული"},
            {"kk","Қазақ Тілі"},
            {"km","ខ្មែរ"},
            {"kn","ಕನ್ನಡ"},
            {"ko","한국어"},
            {"lo","ລາວ"},
            {"lt","Lietuvių"},
            {"lv","Latviešu"},
            {"mi","Te Reo Māori"},
            {"mk","Македонски"},
            {"ml","മലയാളം"},
            {"mr","मराठी"},
            {"ms","Melayu"},
            {"mt","Malti"},
            {"nb","Norsk Bokmål"},
            {"ne","नेपाली"},
            {"nl","Nederlands"},
            {"or","ଓଡ଼ିଆ"},
            {"pa","ਪੰਜਾਬੀ"},
            {"pl","Polski"},
            {"prs","دری"},
            {"pt","Português (Brasil)"},
            {"ro","Română"},
            {"ru","Русский"},
            {"sk","Slovenčina"},
            {"sl","Slovenščina"},
            {"sq","Shqip"},
            {"sr-Latn","Srpski (latinica)"},
            {"sv","Svenska"},
            {"sw","Kiswahili"},
            {"ta","தமிழ்"},
            {"te","తెలుగు"},
            {"th","ไทย"},
            {"ti","ትግር"},
            {"tr","Türkçe"},
            {"uk","Українська"},
            {"ur","اردو"},
            {"vi","Tiếng Việt"},
            {"zh-Hans","中文 (简体)"},
            {"zh-Hant","繁體中文 (繁體)"},
            {"zu","Isi-Zulu"},
        };

        /// <summary>
        /// The languages that come first in the list. Seventy-four entries in alphabetical order means scrolling
        /// past Afrikaans to reach Chinese; the ones people actually pick are at the top, the rest follow in
        /// their usual order.
        /// </summary>
        private static readonly string[] CommonLangs = { "zh-Hans", "zh-Hant", "en", "ja", "ko", "de", "fr", "es", "ru", "pt", "it" };

        /// <summary>Computed on each access: the "follow system" label has to follow the chosen language too,
        /// and the static initializer runs before the language is applied.</summary>
        public static Dictionary<string, string> LangsOptions
        {
            get
            {
                var options = new Dictionary<string, string> { { "default", GetString("UseSystemSetting") } };
                foreach (var lang in CommonLangs)
                    if (SupportedLangs.TryGetValue(lang, out var name))
                        options[lang] = name;
                foreach (var pair in SupportedLangs)
                    if (!options.ContainsKey(pair.Key))
                        options[pair.Key] = pair.Value;
                return options;
            }
        }

        public static string GetLangOptionDisplayName(string key) => LangsOptions[key];

        public static ResourceContext ResourceContext { get; } = Manager.CreateResourceContext();

        /// <summary>
        /// Chooses the language the .resw resources are read in. <c>ApplicationLanguages.PrimaryLanguageOverride</c>
        /// alone is not enough: it needs package identity, so in the installer and portable builds it throws and
        /// the app stays in the system language no matter what the setting says. Setting the language on the
        /// resource contexts works either way — that is what the lookups below actually consult.
        /// </summary>
        /// <summary>The language the interface was actually built with, so the settings can say when it moved on.</summary>
        public static string AppliedLanguage { get; private set; }

        /// <summary>
        /// How dates and times are written: in the interface's language when the reader chose one, so an English window
        /// does not say "周五 9:32"; in the system's regional format when the interface follows the system, as Windows
        /// itself does.
        /// </summary>
        public static System.Globalization.CultureInfo Culture { get; private set; } = System.Globalization.CultureInfo.CurrentCulture;

        public static void ApplyLanguage(string language)
        {
            var wanted = SupportedLangs.ContainsKey(language ?? "") ? language : null;
            AppliedLanguage ??= wanted ?? string.Empty; // the first application is the one the interface was built with
            try { Culture = wanted != null ? FormatCulture.For(wanted) : System.Globalization.CultureInfo.CurrentCulture; }
            catch (System.Globalization.CultureNotFoundException) { Culture = System.Globalization.CultureInfo.CurrentCulture; }
            Apply(ResourceContext, wanted);
            ElementLanguage = wanted ?? SystemLanguageTag();
            Log.Debug($"language: setting={language} applied={wanted ?? "(system)"} resolved={(ResourceContext.QualifierValues.TryGetValue("Language", out var resolved) ? resolved : "")} elements={ElementLanguage}");
            foreach (var handler in LanguageApplied?.GetInvocationList() ?? Array.Empty<Delegate>())
            {
                try { ((Action)handler)(); }
                catch (Exception ex) { Log.Debug($"language: a window could not take {ElementLanguage}: {ex.Message}"); }
            }
        }

        /// <summary>
        /// The language tag the interface's elements carry (FrameworkElement.Language): the chosen language, or the
        /// system's display language when the interface follows the system. WinUI picks the fallback font for Chinese,
        /// Japanese and Korean text by it; an element that carries none takes a default fixed when it (or the popup
        /// window it is shown in) was made, so a menu made while the interface was in English showed Chinese in the
        /// English fallback font after switching back - thinner, other glyph shapes - until the app was restarted.
        /// Null until the first application.
        /// </summary>
        public static string ElementLanguage { get; private set; }

        /// <summary>After every application of the language (the first one too): the windows put the tag on their elements (UiLanguage).</summary>
        public static event Action LanguageApplied;

        private static string SystemLanguageTag()
        {
            try
            {
                var first = Windows.System.UserProfile.GlobalizationPreferences.Languages.FirstOrDefault();
                if (!string.IsNullOrEmpty(first)) return first;
            }
            catch (Exception)
            {
                // not available (no user profile): the process's own below
            }
            return System.Globalization.CultureInfo.CurrentUICulture.Name;
        }

        private static void Apply(ResourceContext context, string language)
        {
            try
            {
                // MRT Core's context has no Reset: the system's languages are its default.
                context.QualifierValues["Language"] = language ?? string.Join(";", Windows.Globalization.ApplicationLanguages.Languages);
            }
            catch (Exception ex)
            {
                Log.Debug($"language: cannot set {language ?? "(system)"}: {ex.Message}");
            }
        }

        public static string GetString(string key, ResourceSource source = 0)
        {
            key = key.Replace('.', '/');
            if (source == 0 || !ResourcesDictionary.ContainsKey(source))
                return ResourcesDictionary.Values.Select(x => x.TryGetValue(key, ResourceContext)?.ValueAsString).Where(x => !string.IsNullOrEmpty(x)).FirstOrDefault();
            return ResourcesDictionary[source].TryGetValue(key, ResourceContext)?.ValueAsString;
        }

        public static string GetDialogString(string key)
        {
            return GetString(key, ResourceSource.DialogResources);
        }

        public static string GetTypeString(Type type)
        {
            return (type.GetCustomAttribute(typeof(LocaleAttribute)) as LocaleAttribute)?.Text;
        }
    }

    public class LocaleAttribute : Attribute
    {
        public string[] Keys { get; }

        public string Text => Texts.FirstOrDefault();

        public IEnumerable<string> Texts => Keys.Select(x => Locale.GetString(x));

        public LocaleAttribute(params string[] keys)
        {
            Keys = keys;
        }
    }

    [MarkupExtensionReturnType(ReturnType = typeof(string))]
    public class LocaleString : MarkupExtension
    {
        public string Key { get; set; }

        public Locale.ResourceSource Source { get; set; }

        protected override object ProvideValue()
        {
            return Locale.GetString(Key, Source);
        }
    }
}
