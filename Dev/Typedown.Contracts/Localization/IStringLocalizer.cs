namespace Typedown.Contracts.Localization;

public enum LocalizationResourceSource
{
    All,
    CommonResources,
    DialogResources,
    SettingsResources,
    Resources,
}

public sealed class LanguageChangedEventArgs : EventArgs
{
    public LanguageChangedEventArgs(string previous, string current)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previous);
        ArgumentException.ThrowIfNullOrWhiteSpace(current);
        Previous = previous;
        Current = current;
    }

    public string Previous { get; }

    public string Current { get; }
}

/// <summary>
/// Resolves application strings without exposing a platform resource API.
/// Implementations observe the shared language setting and notify each window
/// when controls created from localized text must be rebuilt.
/// </summary>
public interface IStringLocalizer
{
    string Language { get; }

    event EventHandler<LanguageChangedEventArgs>? LanguageChanged;

    string GetString(
        string key,
        LocalizationResourceSource source = LocalizationResourceSource.All);
}
