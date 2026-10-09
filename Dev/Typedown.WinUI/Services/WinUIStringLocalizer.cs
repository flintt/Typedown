using System.Globalization;
using Typedown.Contracts.Localization;
using Typedown.Core.Localization;
using Typedown.Core.Services;

namespace Typedown.WinUI.Services;

internal sealed class WinUIStringLocalizer : IStringLocalizer, IDisposable
{
    internal const string LanguageSettingName = "Language";

    private readonly ReswStringCatalog catalog;
    private readonly JsonSettingsStore settings;
    private readonly string systemLanguage;
    private readonly object stateGate = new();
    private string language;
    private int isDisposed;

    public WinUIStringLocalizer(
        ReswStringCatalog catalog,
        JsonSettingsStore settings)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        systemLanguage = CultureInfo.CurrentUICulture.Name;
        language = ResolveConfiguredLanguage();
        settings.Changed += OnSettingsChanged;
    }

    public string Language
    {
        get
        {
            lock (stateGate)
            {
                return language;
            }
        }
    }

    public event EventHandler<LanguageChangedEventArgs>? LanguageChanged;

    public string GetString(
        string key,
        LocalizationResourceSource source = LocalizationResourceSource.All)
    {
        string current;
        lock (stateGate)
        {
            current = language;
        }
        return catalog.GetString(key, current, source);
    }

    private string ResolveConfiguredLanguage() => catalog.ResolveLanguage(
        settings.Get(LanguageSettingName, "default"),
        systemLanguage);

    private void OnSettingsChanged(string? setting, object? origin)
    {
        if (setting is not null && setting != LanguageSettingName)
        {
            return;
        }

        var current = ResolveConfiguredLanguage();
        string previous;
        lock (stateGate)
        {
            if (string.Equals(language, current, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            previous = language;
            language = current;
        }
        LanguageChanged?.Invoke(this, new LanguageChangedEventArgs(previous, current));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }
        settings.Changed -= OnSettingsChanged;
    }
}
