using System.Text.Json;
using System.Text.Json.Serialization;
using Githubie.Application.Configuration;
using Githubie.Application.Repositories;
using Microsoft.Data.Sqlite;

namespace Githubie.Infrastructure.Configuration;

/// <summary>Repository設定をSQLiteへ行単位で永続化します。</summary>
public sealed class SqliteRepositoryConfigurationStore(string databasePath, int busyTimeoutSeconds = 5) : IRepositoryConfigurationStore
{
    private const string LegacyImportKey = "legacy_json_import_v1";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly string _databasePath = Path.GetFullPath(databasePath);
    private readonly int _busyTimeoutSeconds = busyTimeoutSeconds > 0
        ? busyTimeoutSeconds
        : throw new ArgumentOutOfRangeException(nameof(busyTimeoutSeconds));

    /// <summary>登録IDに対応するRepository設定を取得します。</summary>
    public async Task<RepositoryOptions?> GetRepositoryAsync(
        string repositoryId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryId);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT options_json FROM repositories WHERE repository_id = $repository_id;";
            command.Parameters.AddWithValue("$repository_id", repositoryId);
            var json = await command.ExecuteScalarAsync(cancellationToken) as string;
            return json is null ? null : Deserialize(json);
        }
        catch (SqliteException ex)
        {
            throw new IOException("Repository configuration could not be read.", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Repository database contains invalid JSON.", ex);
        }
    }

    /// <summary>Schemaを初期化し、未移行の場合だけJSON由来の登録を取り込みます。</summary>
    public async Task<IReadOnlyDictionary<string, RepositoryOptions>> InitializeAsync(
        IReadOnlyDictionary<string, RepositoryOptions> legacyRepositories,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(legacyRepositories);
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath))
            ?? throw new IOException("Repository database directory could not be resolved.");
        Directory.CreateDirectory(directory);

        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS schema_metadata (
                    key TEXT NOT NULL PRIMARY KEY,
                    value TEXT NOT NULL
                );
                """, cancellationToken, transaction);
            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS repositories (
                    repository_id TEXT NOT NULL PRIMARY KEY,
                    options_json TEXT NOT NULL
                );
                """, cancellationToken, transaction);

            await NormalizeRepositoryIdsAsync(connection, transaction, cancellationToken);

            if (!await HasMetadataAsync(connection, LegacyImportKey, transaction, cancellationToken))
            {
                foreach (var (repositoryId, options) in legacyRepositories)
                {
                    await InsertIfMissingAsync(connection, repositoryId, options, transaction, cancellationToken);
                }

                await WriteMetadataAsync(connection, LegacyImportKey, "completed", transaction, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return await LoadAllAsync(connection, cancellationToken);
        }
        catch (SqliteException ex)
        {
            throw new IOException("Repository database initialization failed.", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Repository database contains invalid JSON.", ex);
        }
    }

    /// <summary>既存Schemaを変更せず、登録済みRepository設定を読み取ります。</summary>
    public async Task<IReadOnlyDictionary<string, RepositoryOptions>> LoadExistingAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_databasePath))
            throw new IOException("Repository database does not exist.");

        try
        {
            await using var connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
            return await LoadAllAsync(connection, cancellationToken);
        }
        catch (SqliteException ex)
        {
            throw new IOException("Repository database could not be read.", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Repository database contains invalid JSON.", ex);
        }
    }

    public async Task SaveRepositoryAsync(
        string repositoryId,
        RepositoryOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryId);
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO repositories (repository_id, options_json)
                VALUES ($repository_id, $options_json)
                ON CONFLICT(repository_id) DO UPDATE SET options_json = excluded.options_json;
                """;
            command.Parameters.AddWithValue("$repository_id", repositoryId);
            command.Parameters.AddWithValue("$options_json", Serialize(options));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex)
        {
            throw new IOException("Repository configuration could not be saved.", ex);
        }
    }

    public async Task DeleteRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryId);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM repositories WHERE repository_id = $repository_id;";
            command.Parameters.AddWithValue("$repository_id", repositoryId);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
                throw new KeyNotFoundException(repositoryId);
        }
        catch (SqliteException ex)
        {
            throw new IOException("Repository configuration could not be deleted.", ex);
        }
    }

    public async Task RenameRepositoryAsync(
        string oldRepositoryId,
        string newRepositoryId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldRepositoryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newRepositoryId);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE repositories
                SET repository_id = $new_repository_id
                WHERE repository_id = $old_repository_id;
                """;
            command.Parameters.AddWithValue("$old_repository_id", oldRepositoryId);
            command.Parameters.AddWithValue("$new_repository_id", newRepositoryId);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
                throw new KeyNotFoundException(oldRepositoryId);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(newRepositoryId, ex);
        }
        catch (SqliteException ex)
        {
            throw new IOException("Repository configuration could not be renamed.", ex);
        }
    }

    private Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken) =>
        OpenAsync(SqliteOpenMode.ReadWriteCreate, cancellationToken);

    private async Task<SqliteConnection> OpenAsync(
        SqliteOpenMode mode,
        CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = mode,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = _busyTimeoutSeconds,
            Pooling = false,
        }.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken);
            await ExecuteAsync(connection, $"PRAGMA busy_timeout = {_busyTimeoutSeconds * 1000};", cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<IReadOnlyDictionary<string, RepositoryOptions>> LoadAllAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var repositories = new Dictionary<string, RepositoryOptions>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT repository_id, options_json FROM repositories ORDER BY repository_id;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var repositoryId = reader.GetString(0);
            var options = JsonSerializer.Deserialize<RepositoryOptions>(reader.GetString(1), SerializerOptions)
                ?? throw new InvalidDataException($"Repository '{repositoryId}' has no options.");
            repositories.Add(repositoryId, options);
        }

        return repositories;
    }

    private static RepositoryOptions Deserialize(string json) =>
        JsonSerializer.Deserialize<RepositoryOptions>(json, SerializerOptions)
        ?? throw new JsonException("Repository options were empty.");

    private static async Task<bool> HasMetadataAsync(
        SqliteConnection connection,
        string key,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM schema_metadata WHERE key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task NormalizeRepositoryIdsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var identifiers = new List<string>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT repository_id FROM repositories;";
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) identifiers.Add(reader.GetString(0));
        }

        foreach (var identifier in identifiers)
        {
            if (!RepositoryId.TryNormalizeLegacy(identifier, out var normalized))
                throw new InvalidDataException($"Repository ID '{identifier}' cannot be normalized.");
            if (string.Equals(identifier, normalized, StringComparison.Ordinal)) continue;

            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE repositories SET repository_id = $normalized WHERE repository_id = $current;";
            update.Parameters.AddWithValue("$normalized", normalized);
            update.Parameters.AddWithValue("$current", identifier);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertIfMissingAsync(
        SqliteConnection connection,
        string repositoryId,
        RepositoryOptions options,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO repositories (repository_id, options_json)
            VALUES ($repository_id, $options_json);
            """;
        command.Parameters.AddWithValue("$repository_id", repositoryId);
        command.Parameters.AddWithValue("$options_json", Serialize(options));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task WriteMetadataAsync(
        SqliteConnection connection,
        string key,
        string value,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO schema_metadata (key, value) VALUES ($key, $value);";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Serialize(RepositoryOptions options) =>
        JsonSerializer.Serialize(options, SerializerOptions);
}
