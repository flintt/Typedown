using Microsoft.Data.Sqlite;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Typedown.Core.Enums;
using Typedown.Core.Models;

namespace Typedown.Core.Services
{
    /// <summary>The app's database (StorageDb) at its place in the app's data folder.</summary>
    public static class AppStorage
    {
        public static StorageDb Db { get; } = new(Path.Combine(Config.GetLocalFolderPath(), "Storage.db"));
    }

    /// <summary>
    /// The export configurations (Settings > Export) and the image upload ones (Settings > Image > Upload) as rows of
    /// the app's database: added, saved, taken out, read. One place for both, where FileExport and ImageUpload had each
    /// their own EF Core code.
    /// </summary>
    public static class ConfigStore
    {
        // ---- export ----

        private static ExportConfig ReadExport(SqliteDataReader r) => new()
        {
            Id = r.GetInt32(0),
            Name = r.IsDBNull(1) ? string.Empty : r.GetString(1),
            Notes = r.IsDBNull(2) ? string.Empty : r.GetString(2),
            Type = (ExportType)r.GetInt32(3),
            Config = r.IsDBNull(4) ? new Newtonsoft.Json.Linq.JObject().ToString() : r.GetString(4),
        };

        private const string ExportColumns = "\"Id\", \"Name\", \"Notes\", \"Type\", \"Config\"";

        public static Task<List<ExportConfig>> ExportConfigsAsync() =>
            AppStorage.Db.QueryAsync($"SELECT {ExportColumns} FROM \"ExportConfig\" ORDER BY \"Id\"", ReadExport);

        public static async Task<ExportConfig> ExportConfigAsync(int id) =>
            (await AppStorage.Db.QueryAsync($"SELECT {ExportColumns} FROM \"ExportConfig\" WHERE \"Id\" = $id", ReadExport, ("$id", id))).FirstOrDefault();

        public static async Task<ExportConfig> AddExportConfigAsync(string name, ExportType type)
        {
            var config = new ExportConfig { Name = name ?? string.Empty, Type = type };
            config.Id = await AppStorage.Db.InsertAsync("INSERT INTO \"ExportConfig\" (\"Name\", \"Notes\", \"Type\", \"Config\") VALUES ($name, $notes, $type, $config)",
                ("$name", config.Name), ("$notes", config.Notes), ("$type", (int)config.Type), ("$config", config.Config));
            return config;
        }

        public static Task SaveExportConfigAsync(ExportConfig config) =>
            AppStorage.Db.ExecuteAsync("UPDATE \"ExportConfig\" SET \"Name\" = $name, \"Notes\" = $notes, \"Type\" = $type, \"Config\" = $config WHERE \"Id\" = $id",
                ("$name", config.Name), ("$notes", config.Notes), ("$type", (int)config.Type), ("$config", config.Config), ("$id", config.Id));

        public static Task RemoveExportConfigAsync(int id) => AppStorage.Db.ExecuteAsync("DELETE FROM \"ExportConfig\" WHERE \"Id\" = $id", ("$id", id));

        public static Task ClearExportConfigsAsync() => AppStorage.Db.ExecuteAsync("DELETE FROM \"ExportConfig\"");

        // ---- image upload ----

        private static ImageUploadConfig ReadUpload(SqliteDataReader r) => new()
        {
            Id = r.GetInt32(0),
            Name = r.IsDBNull(1) ? string.Empty : r.GetString(1),
            Notes = r.IsDBNull(2) ? string.Empty : r.GetString(2),
            IsEnable = r.GetInt64(3) != 0,
            Method = (ImageUploadMethod)r.GetInt32(4),
            Config = r.IsDBNull(5) ? new Newtonsoft.Json.Linq.JObject().ToString() : r.GetString(5),
        };

        private const string UploadColumns = "\"Id\", \"Name\", \"Notes\", \"IsEnable\", \"Method\", \"Config\"";

        public static Task<List<ImageUploadConfig>> UploadConfigsAsync() =>
            AppStorage.Db.QueryAsync($"SELECT {UploadColumns} FROM \"ImageUploadConfig\" ORDER BY \"Id\"", ReadUpload);

        public static async Task<ImageUploadConfig> UploadConfigAsync(int id) =>
            (await AppStorage.Db.QueryAsync($"SELECT {UploadColumns} FROM \"ImageUploadConfig\" WHERE \"Id\" = $id", ReadUpload, ("$id", id))).FirstOrDefault();

        public static async Task<ImageUploadConfig> AddUploadConfigAsync(ImageUploadConfig config)
        {
            config.Id = await AppStorage.Db.InsertAsync("INSERT INTO \"ImageUploadConfig\" (\"Name\", \"Notes\", \"IsEnable\", \"Method\", \"Config\") VALUES ($name, $notes, $enable, $method, $config)",
                ("$name", config.Name), ("$notes", config.Notes), ("$enable", config.IsEnable ? 1 : 0), ("$method", (int)config.Method), ("$config", config.Config));
            return config;
        }

        public static Task SaveUploadConfigAsync(ImageUploadConfig config) =>
            AppStorage.Db.ExecuteAsync("UPDATE \"ImageUploadConfig\" SET \"Name\" = $name, \"Notes\" = $notes, \"IsEnable\" = $enable, \"Method\" = $method, \"Config\" = $config WHERE \"Id\" = $id",
                ("$name", config.Name), ("$notes", config.Notes), ("$enable", config.IsEnable ? 1 : 0), ("$method", (int)config.Method), ("$config", config.Config), ("$id", config.Id));

        public static Task RemoveUploadConfigAsync(int id) => AppStorage.Db.ExecuteAsync("DELETE FROM \"ImageUploadConfig\" WHERE \"Id\" = $id", ("$id", id));

        public static Task ClearUploadConfigsAsync() => AppStorage.Db.ExecuteAsync("DELETE FROM \"ImageUploadConfig\"");
    }
}
