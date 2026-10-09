#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Typedown.Contracts.Localization;

namespace Typedown.Core.Localization;

/// <summary>
/// Reads the existing RESW files through a platform-neutral, cached catalog.
/// The WinUI package carries the files as content so packaged and unpackaged
/// builds use the same lookup behavior.
/// </summary>
public sealed class ReswStringCatalog
{
    private static readonly LocalizationResourceSource[] OrderedSources =
    {
        LocalizationResourceSource.CommonResources,
        LocalizationResourceSource.DialogResources,
        LocalizationResourceSource.SettingsResources,
        LocalizationResourceSource.Resources,
    };

    private static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly string resourcesRoot;
    private readonly IReadOnlyDictionary<string, string> availableLanguages;
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> cache =
        new(StringComparer.OrdinalIgnoreCase);

    public ReswStringCatalog(string resourcesRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcesRoot);
        this.resourcesRoot = Path.GetFullPath(resourcesRoot);
        availableLanguages = Directory.Exists(this.resourcesRoot)
            ? Directory.EnumerateDirectories(this.resourcesRoot)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(name => name!, name => name!, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> AvailableLanguages =>
        availableLanguages.Values.ToArray();

    public string ResolveLanguage(string? requestedLanguage, string? systemLanguage)
    {
        var requested = string.IsNullOrWhiteSpace(requestedLanguage)
            || string.Equals(requestedLanguage, "default", StringComparison.OrdinalIgnoreCase)
                ? systemLanguage
                : requestedLanguage;

        foreach (var candidate in ExpandLanguage(requested))
        {
            if (availableLanguages.TryGetValue(candidate, out var available))
            {
                return available;
            }
        }

        return availableLanguages.TryGetValue("en", out var english)
            ? english
            : availableLanguages.Values.FirstOrDefault() ?? "en";
    }

    public string GetString(
        string key,
        string? language,
        LocalizationResourceSource source = LocalizationResourceSource.All)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var languages = ExpandLanguage(language)
            .Concat(new[] { "en" })
            .Select(candidate => availableLanguages.TryGetValue(candidate, out var available)
                ? available
                : null)
            .Where(candidate => candidate is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>();

        foreach (var candidateLanguage in languages)
        {
            foreach (var candidateSource in GetSources(source))
            {
                var resources = cache.GetOrAdd(
                    $"{candidateLanguage}/{candidateSource}",
                    _ => Load(candidateLanguage, candidateSource));
                if (resources.TryGetValue(key, out var value)
                    && !string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
        }

        return key;
    }

    private static IEnumerable<LocalizationResourceSource> GetSources(
        LocalizationResourceSource source)
    {
        if (source != LocalizationResourceSource.All)
        {
            yield return source;
            yield break;
        }

        foreach (var orderedSource in OrderedSources)
        {
            yield return orderedSource;
        }
    }

    private IEnumerable<string> ExpandLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            yield break;
        }

        var normalized = language.Replace('_', '-');
        yield return normalized;

        if (normalized.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            var traditional = normalized.Contains("Hant", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("-TW", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("-HK", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("-MO", StringComparison.OrdinalIgnoreCase);
            yield return traditional ? "zh-Hant" : "zh-Hans";
        }

        CultureInfo? culture = null;
        try
        {
            culture = CultureInfo.GetCultureInfo(normalized);
        }
        catch (CultureNotFoundException)
        {
        }

        while (culture is not null && !string.IsNullOrWhiteSpace(culture.Name))
        {
            yield return culture.Name;
            culture = culture.Parent;
        }

        var separator = normalized.IndexOf('-');
        if (separator > 0)
        {
            yield return normalized[..separator];
        }
    }

    private IReadOnlyDictionary<string, string> Load(
        string language,
        LocalizationResourceSource source)
    {
        if (source == LocalizationResourceSource.All)
        {
            return Empty;
        }

        var path = Path.Combine(resourcesRoot, language, $"{source}.resw");
        if (!File.Exists(path))
        {
            return Empty;
        }

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            };
            using var reader = XmlReader.Create(path, settings);
            return XDocument.Load(reader, LoadOptions.None)
                .Descendants("data")
                .Select(element => new
                {
                    Key = element.Attribute("name")?.Value,
                    Value = element.Element("value")?.Value,
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                .GroupBy(item => item.Key!, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Value ?? string.Empty,
                    StringComparer.Ordinal);
        }
        catch (XmlException)
        {
            return Empty;
        }
        catch (IOException)
        {
            return Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return Empty;
        }
    }
}
