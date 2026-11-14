using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Typedown.Core.Services
{
    /// <summary>
    /// The app's own database (Storage.db): the export and image upload configurations, the recent files and folders.
    /// Read and written with Microsoft.Data.Sqlite directly - it was EF Core, about 12 MB of the package for four tables
    /// (todo, 安装包体积). The file is the one EF Core made and makes: the same tables, columns and keys, the same text for
    /// times, and its migration history row, so a database moves between this and an earlier version either way (an
    /// earlier version, opening one made here, finds its migration done and changes nothing).
    /// </summary>
    public sealed class StorageDb
    {
        /// <summary>EF Core's one migration, as the history row it writes (Migrations/20230226122314_InitialCreate).</summary>
        public const string MigrationId = "20230226122314_InitialCreate";
        public const string MigrationProductVersion = "3.1.30";

        // EF Core 3.1's SQL for that migration, word for word (Tests/Typedown.ReliabilityTests/Fixtures/legacy-schema.sql):
        // made only where missing, so a database already there is left as it is.
        private const string Schema = @"
CREATE TABLE IF NOT EXISTS ""__EFMigrationsHistory"" (
    ""MigrationId"" TEXT NOT NULL CONSTRAINT ""PK___EFMigrationsHistory"" PRIMARY KEY,
    ""ProductVersion"" TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS ""ExportConfig"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_ExportConfig"" PRIMARY KEY AUTOINCREMENT,
    ""Name"" TEXT NULL,
    ""Notes"" TEXT NULL,
    ""Type"" INTEGER NOT NULL,
    ""Config"" TEXT NULL
);
CREATE TABLE IF NOT EXISTS ""FileAccessHistory"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_FileAccessHistory"" PRIMARY KEY AUTOINCREMENT,
    ""AccessTime"" TEXT NOT NULL,
    ""FilePath"" TEXT NULL
);
CREATE TABLE IF NOT EXISTS ""FolderAccessHistory"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_FolderAccessHistory"" PRIMARY KEY AUTOINCREMENT,
    ""AccessTime"" TEXT NOT NULL,
    ""FolderPath"" TEXT NULL
);
CREATE TABLE IF NOT EXISTS ""ImageUploadConfig"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_ImageUploadConfig"" PRIMARY KEY AUTOINCREMENT,
    ""Name"" TEXT NULL,
    ""Notes"" TEXT NULL,
    ""IsEnable"" INTEGER NOT NULL,
    ""Method"" INTEGER NOT NULL,
    ""Config"" TEXT NULL
);
INSERT OR IGNORE INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"") VALUES ($migration, $version);";

        /// <summary>A time as EF Core wrote it to the database (its SQLite type mapping): sorts as text.</summary>
        public const string TimeFormat = "yyyy-MM-dd HH:mm:ss.FFFFFFF";

        private readonly string connectionString;
        private Task schema;
        private readonly object sync = new();

        public StorageDb(string path)
        {
            connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        }

        /// <summary>A connection to the database, its tables made first (once per instance).</summary>
        public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
        {
            lock (sync) schema ??= EnsureSchemaAsync();
            await schema;
            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }

        private async Task EnsureSchemaAsync()
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = Schema;
                command.Parameters.AddWithValue("$migration", MigrationId);
                command.Parameters.AddWithValue("$version", MigrationProductVersion);
                await command.ExecuteNonQueryAsync();
            }
            transaction.Commit();
        }

        /// <summary>A statement run; the rows it changed.</summary>
        public async Task<int> ExecuteAsync(string sql, params (string name, object value)[] parameters)
        {
            using var connection = await OpenAsync();
            using var command = Command(connection, sql, parameters);
            return await command.ExecuteNonQueryAsync();
        }

        /// <summary>A row inserted; its new Id (the table's AUTOINCREMENT key).</summary>
        public async Task<int> InsertAsync(string sql, params (string name, object value)[] parameters)
        {
            using var connection = await OpenAsync();
            using var command = Command(connection, sql + "; SELECT last_insert_rowid();", parameters);
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        /// <summary>The rows of a query, each made into a <typeparamref name="T"/>.</summary>
        public async Task<List<T>> QueryAsync<T>(string sql, Func<SqliteDataReader, T> read, params (string name, object value)[] parameters)
        {
            var rows = new List<T>();
            using var connection = await OpenAsync();
            using var command = Command(connection, sql, parameters);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) rows.Add(read(reader));
            return rows;
        }

        private static SqliteCommand Command(SqliteConnection connection, string sql, (string name, object value)[] parameters)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            return command;
        }

        // ---- the recent files and folders ----

        /// <summary>Which recent list: its table and its path column.</summary>
        public enum Recent { Files, Folders }

        private static (string table, string column) Of(Recent recent) => recent == Recent.Files
            ? ("\"FileAccessHistory\"", "\"FilePath\"")
            : ("\"FolderAccessHistory\"", "\"FolderPath\"");

        /// <summary>
        /// A path opened now: its row's time moved on, or a row for it - one row per path, not one per opening (the
        /// table grew without bound, and the latest rows were many of one file) - and only the latest <paramref name="keep"/> rows kept.
        /// </summary>
        public async Task RecordAsync(Recent recent, string path, DateTime time, int keep)
        {
            var (table, column) = Of(recent);
            using var connection = await OpenAsync();
            using var transaction = connection.BeginTransaction();
            var at = time.ToString(TimeFormat, System.Globalization.CultureInfo.InvariantCulture);
            using (var update = Command(connection, $"UPDATE {table} SET \"AccessTime\" = $at WHERE {column} = $path", new[] { ("$at", (object)at), ("$path", path) }))
            {
                update.Transaction = transaction;
                if (await update.ExecuteNonQueryAsync() == 0)
                {
                    using var insert = Command(connection, $"INSERT INTO {table} (\"AccessTime\", {column}) VALUES ($at, $path)", new[] { ("$at", (object)at), ("$path", path) });
                    insert.Transaction = transaction;
                    await insert.ExecuteNonQueryAsync();
                }
            }
            using (var trim = Command(connection, $"DELETE FROM {table} WHERE \"Id\" NOT IN (SELECT \"Id\" FROM {table} ORDER BY \"AccessTime\" DESC LIMIT $keep)", new[] { ("$keep", (object)keep) }))
            {
                trim.Transaction = transaction;
                await trim.ExecuteNonQueryAsync();
            }
            transaction.Commit();
        }

        /// <summary>A path taken off its list.</summary>
        public Task RemoveAsync(Recent recent, string path)
        {
            var (table, column) = Of(recent);
            return ExecuteAsync($"DELETE FROM {table} WHERE {column} = $path", ("$path", path));
        }

        /// <summary>The list emptied.</summary>
        public Task ClearAsync(Recent recent) => ExecuteAsync($"DELETE FROM {Of(recent).table}");

        /// <summary>The latest <paramref name="count"/> paths, the latest first.</summary>
        public Task<List<string>> LatestAsync(Recent recent, int count)
        {
            var (table, column) = Of(recent);
            return QueryAsync($"SELECT {column} FROM {table} ORDER BY \"AccessTime\" DESC LIMIT $count", r => r.IsDBNull(0) ? null : r.GetString(0), ("$count", count));
        }
    }
}
