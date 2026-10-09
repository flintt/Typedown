using Typedown.Contracts.Platform;
using Windows.ApplicationModel;
using Windows.Storage;

namespace Typedown.WinUI.Services;

internal sealed class WinUIAppDataPathProvider : IAppDataPathProvider
{
    public WinUIAppDataPathProvider(string? appName = null)
    {
        appName ??= typeof(WinUIAppDataPathProvider).Assembly.GetName().Name;
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);

        PersistentDataDirectory = ResolvePersistentDataDirectory(appName);
        CacheDirectory = ResolveCacheDirectory(appName);
        SettingsFilePath = Path.Combine(PersistentDataDirectory, "Settings.json");
        DatabaseFilePath = Path.Combine(PersistentDataDirectory, "Storage.db");
        BackupDirectory = Path.Combine(PersistentDataDirectory, "Backup");
        ThemesDirectory = Path.Combine(PersistentDataDirectory, "themes");
        SessionFilePath = Path.Combine(PersistentDataDirectory, "session.json");
        CursorFilePath = Path.Combine(PersistentDataDirectory, "cursors.json");
        ImageUploadHistoryFilePath = Path.Combine(PersistentDataDirectory, "ImageUploadHistory.json");
        HedgeDocSharesFilePath = Path.Combine(PersistentDataDirectory, "hedgedoc-shares.json");

        Directory.CreateDirectory(PersistentDataDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(BackupDirectory);
        Directory.CreateDirectory(ThemesDirectory);
    }

    public string PersistentDataDirectory { get; }

    public string CacheDirectory { get; }

    public string SettingsFilePath { get; }

    public string DatabaseFilePath { get; }

    public string BackupDirectory { get; }

    public string ThemesDirectory { get; }

    public string SessionFilePath { get; }

    public string CursorFilePath { get; }

    public string ImageUploadHistoryFilePath { get; }

    public string HedgeDocSharesFilePath { get; }

    private static string ResolvePersistentDataDirectory(string appName)
    {
        try
        {
            if (Package.Current is not null)
            {
                var packagedPath = ApplicationData.Current.LocalFolder.Path;
                if (!string.IsNullOrWhiteSpace(packagedPath))
                {
                    return Path.GetFullPath(packagedPath);
                }
            }
        }
        catch
        {
            // An unpackaged process has no Package/ApplicationData identity.
        }

        return CombineSpecialFolder(Environment.SpecialFolder.MyDocuments, appName);
    }

    private static string ResolveCacheDirectory(string appName)
    {
        return CombineSpecialFolder(Environment.SpecialFolder.LocalApplicationData, appName);
    }

    private static string CombineSpecialFolder(
        Environment.SpecialFolder specialFolder,
        string appName)
    {
        var root = Environment.GetFolderPath(specialFolder);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException(
                $"Windows did not provide a path for {specialFolder}.");
        }

        return Path.GetFullPath(Path.Combine(root, appName));
    }
}
