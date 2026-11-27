using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Githubie.Application.Publishing;
using Microsoft.Data.Sqlite;

namespace Githubie.Infrastructure.Repositories;

/// <summary>Durable in-flight records prevent a restarted service from blindly replaying mutations.</summary>
public sealed class SqlitePrPublicationStore(string databasePath) : IPrPublicationStore
{
    private readonly string _path = Path.GetFullPath(databasePath);

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _path, Mode = SqliteOpenMode.ReadWriteCreate, DefaultTimeout = 5, Pooling = false,
        }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS pr_publication_operations (
                repository TEXT NOT NULL COLLATE NOCASE,
                operation_id TEXT NOT NULL,
                payload TEXT NOT NULL,
                PRIMARY KEY (repository, operation_id)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task<IAsyncDisposable?> TryAcquireAsync(string repository, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string directory = _path + ".pr-publication-locks";
        Directory.CreateDirectory(directory);
        string name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(repository.ToLowerInvariant())));
        try
        {
            IAsyncDisposable lease = new FileStream(Path.Combine(directory, name + ".lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return Task.FromResult<IAsyncDisposable?>(lease);
        }
        catch (IOException) { return Task.FromResult<IAsyncDisposable?>(null); }
    }

    public async Task<PrPublicationRecord?> GetAsync(string repository, string operationId, CancellationToken cancellationToken)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM pr_publication_operations WHERE repository=$repo AND operation_id=$id";
        command.Parameters.AddWithValue("$repo", repository);
        command.Parameters.AddWithValue("$id", operationId);
        var payload = await command.ExecuteScalarAsync(cancellationToken);
        return payload is string json ? JsonSerializer.Deserialize<PrPublicationRecord>(json)
            ?? throw new InvalidDataException("Invalid publication record.") : null;
    }

    public async Task<bool> HasUnresolvedAsync(string repository, string sourceBranch, CancellationToken cancellationToken)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(SELECT 1 FROM pr_publication_operations
                WHERE repository=$repo AND json_extract(payload,'$.Result.Data.SourceBranch')=$source
                AND (json_extract(payload,'$.Result.Data.PublicationState')='unknown'
                    OR json_extract(payload,'$.Result.Data.PullRequestState')='unknown'));
            """;
        command.Parameters.AddWithValue("$repo", repository);
        command.Parameters.AddWithValue("$source", sourceBranch);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture) != 0;
    }

    public async Task PutAsync(string repository, string operationId, PrPublicationRecord record, CancellationToken cancellationToken)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO pr_publication_operations(repository,operation_id,payload) VALUES($repo,$id,$payload)
            ON CONFLICT(repository,operation_id) DO UPDATE SET payload=excluded.payload;
            """;
        command.Parameters.AddWithValue("$repo", repository);
        command.Parameters.AddWithValue("$id", operationId);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(record));
        await command.ExecuteNonQueryAsync(cancellationToken);
        transaction.Commit();
    }
}
