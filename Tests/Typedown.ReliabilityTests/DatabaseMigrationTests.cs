using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Typedown.Core.Services;

namespace Typedown.ReliabilityTests
{
    [TestClass]
    public class DatabaseMigrationTests
    {
        private static readonly string[] ExpectedTables =
        {
            "ExportConfig", "FileAccessHistory", "FolderAccessHistory", "ImageUploadConfig", "__EFMigrationsHistory"
        };

        [TestMethod]
        public async Task CurrentMigrationsCreateTheExpectedSchemaFromEmpty()
        {
            using var directory = new TestDirectory();
            var path = directory.File("Storage.db");

            using (var context = new AppDbContext(path))
                await context.Database.MigrateAsync();

            CollectionAssert.AreEquivalent(ExpectedTables, await ReadNames(path, "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'"));
            CollectionAssert.AreEquivalent(new[] { "Id", "Name", "Notes", "Type", "Config" }, await ReadPragmaNames(path, "ExportConfig"));
            CollectionAssert.AreEquivalent(new[] { "Id", "AccessTime", "FilePath" }, await ReadPragmaNames(path, "FileAccessHistory"));
            CollectionAssert.AreEquivalent(new[] { "Id", "AccessTime", "FolderPath" }, await ReadPragmaNames(path, "FolderAccessHistory"));
            CollectionAssert.AreEquivalent(new[] { "Id", "Name", "Notes", "IsEnable", "Method", "Config" }, await ReadPragmaNames(path, "ImageUploadConfig"));
            Assert.AreEqual(1L, await ScalarLong(path, "SELECT COUNT(*) FROM __EFMigrationsHistory"));
            Assert.AreEqual("ok", await ScalarString(path, "PRAGMA integrity_check"));
        }

        [TestMethod]
        public async Task LegacyDatabaseUpgradesWithoutLosingDataAndMigrationIsIdempotent()
        {
            using var directory = new TestDirectory();
            var path = directory.File("Storage.db");
            await CreateFromFixture(path, "legacy-schema.sql");

            using (var context = new AppDbContext(path))
                await context.Database.MigrateAsync();
            using (var context = new AppDbContext(path))
            {
                await context.Database.MigrateAsync();
                Assert.AreEqual(0, (await context.Database.GetPendingMigrationsAsync()).Count());
            }

            CollectionAssert.AreEquivalent(ExpectedTables, await ReadNames(path, "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'"));
            Assert.AreEqual(@"C:\docs\legacy.md", await ScalarString(path, "SELECT FilePath FROM FileAccessHistory WHERE Id=11"));
            Assert.AreEqual(@"C:\docs", await ScalarString(path, "SELECT FolderPath FROM FolderAccessHistory WHERE Id=12"));
            Assert.AreEqual("Legacy PDF", await ScalarString(path, "SELECT Name FROM ExportConfig WHERE Id=13"));
            Assert.AreEqual("Legacy upload", await ScalarString(path, "SELECT Name FROM ImageUploadConfig WHERE Id=14"));
            Assert.AreEqual(1L, await ScalarLong(path, "SELECT COUNT(*) FROM __EFMigrationsHistory"));
            Assert.AreEqual("ok", await ScalarString(path, "PRAGMA integrity_check"));

            // INTEGER PRIMARY KEY is SQLite's durable row-identity index. Future schema changes must keep it.
            foreach (var table in ExpectedTables.Where(name => !name.StartsWith("__", StringComparison.Ordinal)))
                Assert.AreEqual("Id", await PrimaryKeyColumn(path, table), $"{table} lost its primary-key index");
        }

        // ---- StorageDb, the app's own access since EF Core went: the same file either way ----

        [TestMethod]
        public async Task StorageDbMakesTheSchemaEfCoreMadeAndAnEarlierVersionTakesIt()
        {
            using var directory = new TestDirectory();
            var path = directory.File("Storage.db");
            using (await new StorageDb(path).OpenAsync()) { }

            CollectionAssert.AreEquivalent(ExpectedTables, await ReadNames(path, "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'"));
            CollectionAssert.AreEquivalent(new[] { "Id", "Name", "Notes", "Type", "Config" }, await ReadPragmaNames(path, "ExportConfig"));
            CollectionAssert.AreEquivalent(new[] { "Id", "AccessTime", "FilePath" }, await ReadPragmaNames(path, "FileAccessHistory"));
            CollectionAssert.AreEquivalent(new[] { "Id", "AccessTime", "FolderPath" }, await ReadPragmaNames(path, "FolderAccessHistory"));
            CollectionAssert.AreEquivalent(new[] { "Id", "Name", "Notes", "IsEnable", "Method", "Config" }, await ReadPragmaNames(path, "ImageUploadConfig"));
            foreach (var table in ExpectedTables.Where(name => !name.StartsWith("__", StringComparison.Ordinal)))
                Assert.AreEqual("Id", await PrimaryKeyColumn(path, table), $"{table}'s key");
            Assert.AreEqual(StorageDb.MigrationId, await ScalarString(path, "SELECT MigrationId FROM __EFMigrationsHistory"));

            // An earlier version (EF Core) opening it: its migration found done, nothing to make, nothing thrown.
            using (var context = new AppDbContext(path))
            {
                await context.Database.MigrateAsync();
                Assert.AreEqual(0, (await context.Database.GetPendingMigrationsAsync()).Count());
                context.FileAccessHistories.Add(new Typedown.Core.Models.FileAccessHistory { FilePath = @"C:\docs\old-version.md", AccessTime = new DateTime(2025, 5, 6, 7, 8, 9) });
                await context.SaveChangesAsync();
            }
            // And back here: the earlier version's row read.
            CollectionAssert.AreEqual(new[] { @"C:\docs\old-version.md" }, await new StorageDb(path).LatestAsync(StorageDb.Recent.Files, 10));
            Assert.AreEqual("ok", await ScalarString(path, "PRAGMA integrity_check"));
        }

        [TestMethod]
        public async Task StorageDbReadsAndWritesADatabaseEfCoreMade()
        {
            using var directory = new TestDirectory();
            var path = directory.File("Storage.db");
            await CreateFromFixture(path, "legacy-schema.sql");
            var db = new StorageDb(path);

            CollectionAssert.AreEqual(new[] { @"C:\docs\legacy.md" }, await db.LatestAsync(StorageDb.Recent.Files, 10));
            CollectionAssert.AreEqual(new[] { @"C:\docs" }, await db.LatestAsync(StorageDb.Recent.Folders, 10));
            await db.RecordAsync(StorageDb.Recent.Files, @"C:\docs\new.md", new DateTime(2026, 11, 14, 9, 30, 0), keep: 100);
            CollectionAssert.AreEqual(new[] { @"C:\docs\new.md", @"C:\docs\legacy.md" }, await db.LatestAsync(StorageDb.Recent.Files, 10));

            var id = await db.InsertAsync("INSERT INTO \"ExportConfig\" (\"Name\", \"Notes\", \"Type\", \"Config\") VALUES ($n, '', 1, '{}')", ("$n", "HTML"));
            Assert.IsTrue(id > 13, $"a new configuration's id after the ones there ({id})");
            var names = await db.QueryAsync("SELECT \"Name\" FROM \"ExportConfig\" ORDER BY \"Id\"", r => r.GetString(0));
            CollectionAssert.AreEqual(new[] { "Legacy PDF", "HTML" }, names);
            Assert.AreEqual("Legacy upload", await ScalarString(path, "SELECT Name FROM ImageUploadConfig WHERE Id=14"));

            // The earlier version reads what was written here: its rows and their times.
            using var context = new AppDbContext(path);
            await context.Database.MigrateAsync();
            var rows = await context.FileAccessHistories.OrderByDescending(x => x.AccessTime).ToListAsync();
            Assert.AreEqual(@"C:\docs\new.md", rows[0].FilePath);
            Assert.AreEqual(new DateTime(2026, 11, 14, 9, 30, 0), rows[0].AccessTime);
            Assert.AreEqual(2, context.ExportConfigs.Count());
        }

        [TestMethod]
        public async Task RecentPathsKeepOneRowEachAndOnlyTheLatest()
        {
            using var directory = new TestDirectory();
            var db = new StorageDb(directory.File("Storage.db"));
            var start = new DateTime(2026, 1, 1, 12, 0, 0);
            await db.RecordAsync(StorageDb.Recent.Files, @"C:\a.md", start, keep: 3);
            await db.RecordAsync(StorageDb.Recent.Files, @"C:\a.md", start.AddMinutes(1), keep: 3);
            Assert.AreEqual(1L, await ScalarLong(directory.File("Storage.db"), "SELECT COUNT(*) FROM FileAccessHistory"), "one row per path");
            for (var i = 0; i < 5; i++)
                await db.RecordAsync(StorageDb.Recent.Files, $@"C:\f{i}.md", start.AddMinutes(10 + i), keep: 3);
            CollectionAssert.AreEqual(new[] { @"C:\f4.md", @"C:\f3.md", @"C:\f2.md" }, await db.LatestAsync(StorageDb.Recent.Files, 10), "the latest three kept");
            // Opened again: back to the top, not a second row.
            await db.RecordAsync(StorageDb.Recent.Files, @"C:\f2.md", start.AddMinutes(30), keep: 3);
            CollectionAssert.AreEqual(new[] { @"C:\f2.md", @"C:\f4.md", @"C:\f3.md" }, await db.LatestAsync(StorageDb.Recent.Files, 10));
            await db.RemoveAsync(StorageDb.Recent.Files, @"C:\f4.md");
            CollectionAssert.AreEqual(new[] { @"C:\f2.md", @"C:\f3.md" }, await db.LatestAsync(StorageDb.Recent.Files, 10));
            await db.ClearAsync(StorageDb.Recent.Files);
            Assert.AreEqual(0, (await db.LatestAsync(StorageDb.Recent.Files, 10)).Count);
            // A path with a quote and a semicolon is a value, not SQL.
            await db.RecordAsync(StorageDb.Recent.Folders, @"C:\it's; DROP TABLE x", start, keep: 3);
            CollectionAssert.AreEqual(new[] { @"C:\it's; DROP TABLE x" }, await db.LatestAsync(StorageDb.Recent.Folders, 10));
        }

        [TestMethod]
        public async Task TimesAreWrittenAsEfCoreWritesThem()
        {
            using var directory = new TestDirectory();
            var efPath = directory.File("ef.db");
            var time = new DateTime(2024, 1, 2, 3, 4, 5, 678);
            using (var context = new AppDbContext(efPath))
            {
                await context.Database.MigrateAsync();
                context.FileAccessHistories.Add(new Typedown.Core.Models.FileAccessHistory { FilePath = "x", AccessTime = time });
                context.FileAccessHistories.Add(new Typedown.Core.Models.FileAccessHistory { FilePath = "y", AccessTime = new DateTime(2024, 1, 2, 3, 4, 5) });
                await context.SaveChangesAsync();
            }
            var hereDb = new StorageDb(directory.File("here.db"));
            await hereDb.RecordAsync(StorageDb.Recent.Files, "x", time, keep: 10);
            await hereDb.RecordAsync(StorageDb.Recent.Files, "y", new DateTime(2024, 1, 2, 3, 4, 5), keep: 10);
            foreach (var p in new[] { "x", "y" })
                Assert.AreEqual(await ScalarString(efPath, $"SELECT AccessTime FROM FileAccessHistory WHERE FilePath='{p}'"),
                    await ScalarString(directory.File("here.db"), $"SELECT AccessTime FROM FileAccessHistory WHERE FilePath='{p}'"), $"{p}'s time as text");
        }

        private static async Task CreateFromFixture(string path, string fixture)
        {
            using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<string[]> ReadNames(string path, string sql)
        {
            var names = new List<string>();
            using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) names.Add(reader.GetString(0));
            return names.ToArray();
        }

        private static async Task<string[]> ReadPragmaNames(string path, string table)
        {
            var names = new List<string>();
            using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info('{table.Replace("'", "''")}')";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) names.Add(reader.GetString(1));
            return names.ToArray();
        }

        private static async Task<long> ScalarLong(string path, string sql) => Convert.ToInt64(await Scalar(path, sql));
        private static async Task<string> ScalarString(string path, string sql) => Convert.ToString(await Scalar(path, sql));

        private static async Task<object> Scalar(string path, string sql)
        {
            using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return await command.ExecuteScalarAsync();
        }

        private static async Task<string> PrimaryKeyColumn(string path, string table)
        {
            using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info('{table.Replace("'", "''")}')";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                if (reader.GetInt32(5) > 0) return reader.GetString(1);
            return null;
        }
    }
}
