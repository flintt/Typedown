using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Windows.ApplicationModel.Resources;
using Windows.ApplicationModel.Resources.Core;
using Windows.UI.Xaml.Markup;

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

        public static IReadOnlyDictionary<ResourceSource, ResourceMap> ResourcesDictionary = new Dictionary<ResourceSource, ResourceMap>()
        {
            {ResourceSource.CommonResources,  ResourceManager.Current.MainResourceMap.GetSubtree($"{assemblyName}/" + nameof(ResourceSource.CommonResources))},
            {ResourceSource.DialogResources,  ResourceManager.Current.MainResourceMap.GetSubtree($"{assemblyName}/" + nameof(ResourceSource.DialogResources))},
            {ResourceSource.SettingsResources,  ResourceManager.Current.MainResourceMap.GetSubtree($"{assemblyName}/" + nameof(ResourceSource.SettingsResources))},
            {ResourceSource.Resources,  ResourceManager.Current.MainResourceMap.GetSubtree($"{assemblyName}/" + nameof(ResourceSource.Resources))}
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

        public static ResourceContext ResourceContext { get; } = new();

        /// <summary>
        /// Chooses the language the .resw resources are read in. <c>ApplicationLanguages.PrimaryLanguageOverride</c>
        /// alone is not enough: it needs package identity, so in the installer and portable builds it throws and
        /// the app stays in the system language no matter what the setting says. Setting the language on the
        /// resource contexts works either way — that is what the lookups below actually consult.
        /// </summary>
        /// <summary>The language the interface was actually built with, so the settings can say when it moved on.</summary>
        public static string AppliedLanguage { get; private set; }

        public static void ApplyLanguage(string language)
        {
            var wanted = SupportedLangs.ContainsKey(language ?? "") ? language : null;
            AppliedLanguage ??= wanted ?? string.Empty; // the first application is the one the interface was built with
            Apply(ResourceContext, wanted);
            try
            {
                Apply(ResourceManager.Current.DefaultContext, wanted);
            }
            catch (Exception ex)
            {
                Log.Debug($"language: default context refused {wanted ?? "(system)"}: {ex.Message}");
            }
            Log.Debug($"language: setting={language} applied={wanted ?? "(system)"} resolved={string.Join(",", ResourceContext.Languages)}");
        }

        private static void Apply(ResourceContext context, string language)
        {
            try
            {
                if (language == null)
                    context.Reset();
                else
                    context.QualifierValues["Language"] = language;
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
                return ResourcesDictionary.Values.Select(x => x.GetValue(key, ResourceContext)?.ValueAsString).Where(x => !string.IsNullOrEmpty(x)).FirstOrDefault();
            return ResourcesDictionary[source].GetValue(key, ResourceContext)?.ValueAsString;
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
            var text = Locale.GetString(Key, Source);
            if (!string.IsNullOrEmpty(text))
                lock (provided) provided[text] = (Key, Source);
            return text;
        }

        // Every text handed out, in every language so far, and the key it came from: what Refresh recognises.
        private static readonly Dictionary<string, (string Key, Locale.ResourceSource Source)> provided = new();

        // The properties text from here goes into, in this app's XAML.
        private static readonly Windows.UI.Xaml.DependencyProperty[] TextProperties =
        {
            Windows.UI.Xaml.Controls.ContentControl.ContentProperty,
            Windows.UI.Xaml.Controls.TextBlock.TextProperty,
            Windows.UI.Xaml.Controls.ToolTipService.ToolTipProperty,
            Windows.UI.Xaml.Controls.TextBox.PlaceholderTextProperty,
            Windows.UI.Xaml.Controls.TextBox.HeaderProperty,
            Windows.UI.Xaml.Controls.ComboBox.PlaceholderTextProperty,
            Windows.UI.Xaml.Controls.ComboBox.HeaderProperty,
            Windows.UI.Xaml.Controls.ToggleSwitch.HeaderProperty,
            Windows.UI.Xaml.Automation.AutomationProperties.NameProperty,
        };

        /// <summary>
        /// Puts what an element tree shows from here into the language now applied. The text goes into a property
        /// once, when the XAML loads: leaving the settings page builds a new main page, so changing the language
        /// there reaches everything, but a change from elsewhere (another window, the automation API) left the
        /// main page - the side pane's tabs, the folder and outline pages, the status bar - in the old language.
        /// A property counts when it holds exactly a text handed out from here; the document is in the editor's
        /// web page, not in this tree, and a text box's own text is not one of the properties.
        /// </summary>
        public static void Refresh(Windows.UI.Xaml.DependencyObject root)
        {
            Dictionary<string, (string Key, Locale.ResourceSource Source)> known;
            lock (provided) known = new(provided);
            var changed = 0;
            var pending = new Stack<Windows.UI.Xaml.DependencyObject>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var element = pending.Pop();
                foreach (var property in TextProperties)
                {
                    try
                    {
                        if (element.ReadLocalValue(property) is string old && known.TryGetValue(old, out var from))
                        {
                            var text = Locale.GetString(from.Key, from.Source);
                            if (!string.IsNullOrEmpty(text) && text != old) { element.SetValue(property, text); changed++; }
                        }
                    }
                    catch (Exception) { /* not a property of this element */ }
                }
                var count = Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element);
                for (var i = 0; i < count; i++) pending.Push(Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(element, i));
            }
            Log.Debug($"language: {changed} text(s) on the page put into the new language");
        }
    }
}
