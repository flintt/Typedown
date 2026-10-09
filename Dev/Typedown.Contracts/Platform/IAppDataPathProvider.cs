namespace Typedown.Contracts.Platform;

/// <summary>
/// Supplies all application-owned paths without binding callers to a host API.
/// Implementations must return absolute paths and keep persistent data separate
/// from data that may be deleted by the host.
/// </summary>
public interface IAppDataPathProvider
{
    string PersistentDataDirectory { get; }

    string CacheDirectory { get; }

    string SettingsFilePath { get; }

    string DatabaseFilePath { get; }

    string BackupDirectory { get; }

    string ThemesDirectory { get; }
}
