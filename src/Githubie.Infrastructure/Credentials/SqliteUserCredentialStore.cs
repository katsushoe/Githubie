using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Githubie.Application.Configuration;
using Githubie.Application.Credentials;
using Githubie.Application.Repositories;
using Microsoft.Data.Sqlite;

namespace Githubie.Infrastructure.Credentials;

/// <summary>ユーザープロフィールのSQLite内に保護済み資格を保存します。設定との新規同時登録も提供します。</summary>
public sealed class SqliteUserCredentialStore(string databasePath, IDpapiProtector protector)
    : IApiTokenStore, IAtomicRepositoryRegistrationStore, IAtomicRepositoryManagementStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Githubie/UserProfile/Credential/v2");
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(databasePath), Mode = SqliteOpenMode.ReadWrite, DefaultTimeout = 5, Pooling = false,
    }.ToString();

    public async Task SaveRegistrationAsync(string repositoryId, RepositoryOptions options, char[]? token,
        CancellationToken cancellationToken)
    {
        if (!RepositoryId.TryNormalize(repositoryId, out repositoryId)) throw new ArgumentException("Invalid repository ID.");
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            var encrypted = token is null ? null : Protect(token);
            await using var connection = Open();
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO repositories (repository_id, options_json) VALUES ($id, $options);";
            command.Parameters.AddWithValue("$id", repositoryId);
            command.Parameters.AddWithValue("$options", JsonSerializer.Serialize(options, Json));
            await command.ExecuteNonQueryAsync(cancellationToken);
            if (encrypted is not null)
            {
                command.CommandText = "INSERT INTO user_credentials (repository_id, protected_token) VALUES ($id, $token) ON CONFLICT(repository_id) DO UPDATE SET protected_token = excluded.protected_token;";
                command.Parameters.Clear();
                command.Parameters.AddWithValue("$id", repositoryId);
                command.Parameters.AddWithValue("$token", encrypted);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                command.CommandText = "DELETE FROM user_credentials WHERE repository_id = $id;";
                command.Parameters.Clear();
                command.Parameters.AddWithValue("$id", repositoryId);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is SqliteException or CryptographicException)
        {
            throw new IOException("Registration could not be committed atomically.", ex);
        }
    }

    public ApiTokenStoreResult Save(string repositoryId, ReadOnlySpan<char> token)
    {
        if (!RepositoryId.TryNormalize(repositoryId, out repositoryId)) return ApiTokenStoreResult.Failure(ApiTokenStoreError.InvalidRepositoryId);
        if (token.IsEmpty) return ApiTokenStoreResult.Failure(ApiTokenStoreError.TokenEmpty);
        if (token.Length > 2560) return ApiTokenStoreResult.Failure(ApiTokenStoreError.TokenTooLong);
        try
        {
            var encrypted = Protect(token);
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO user_credentials (repository_id, protected_token) VALUES ($id, $token) ON CONFLICT(repository_id) DO UPDATE SET protected_token = excluded.protected_token;";
            command.Parameters.AddWithValue("$id", repositoryId);
            command.Parameters.AddWithValue("$token", encrypted);
            command.ExecuteNonQuery();
            return ApiTokenStoreResult.Success();
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or CryptographicException)
        {
            return ApiTokenStoreResult.Failure(ApiTokenStoreError.IoError);
        }
    }

    public ApiTokenStoreReadResult Read(string repositoryId)
    {
        if (!RepositoryId.TryNormalize(repositoryId, out repositoryId)) return ApiTokenStoreReadResult.Failure(ApiTokenStoreError.InvalidRepositoryId);
        byte[]? plain = null;
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT protected_token FROM user_credentials WHERE repository_id = $id;";
            command.Parameters.AddWithValue("$id", repositoryId);
            if (command.ExecuteScalar() is not byte[] encrypted) return ApiTokenStoreReadResult.Failure(ApiTokenStoreError.TokenNotFound);
            plain = protector.Unprotect(encrypted, Entropy);
            return ApiTokenStoreReadResult.Success(Encoding.UTF8.GetChars(plain));
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or CryptographicException)
        {
            return ApiTokenStoreReadResult.Failure(ApiTokenStoreError.IoError);
        }
        finally { if (plain is not null) CryptographicOperations.ZeroMemory(plain); }
    }

    public ApiTokenStoreResult Delete(string repositoryId) => Mutate(repositoryId, null);
    public ApiTokenStoreResult Rename(string oldRepositoryId, string newRepositoryId) => Mutate(oldRepositoryId, newRepositoryId);

    public Task RenameRegistrationAsync(string oldRepositoryId, string newRepositoryId, CancellationToken cancellationToken) =>
        MutateRegistrationAsync(oldRepositoryId, newRepositoryId, cancellationToken);

    public Task DeleteRegistrationAsync(string repositoryId, CancellationToken cancellationToken) =>
        MutateRegistrationAsync(repositoryId, null, cancellationToken);

    private async Task MutateRegistrationAsync(string repositoryId, string? newId, CancellationToken cancellationToken)
    {
        if (!RepositoryId.TryNormalize(repositoryId, out repositoryId)
            || (newId is not null && !RepositoryId.TryNormalize(newId, out newId))) throw new ArgumentException("Invalid repository ID.");
        try
        {
            await using var connection = Open();
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = newId is null ? "DELETE FROM repositories WHERE repository_id = $id;"
                : "UPDATE repositories SET repository_id = $new WHERE repository_id = $id;";
            command.Parameters.AddWithValue("$id", repositoryId);
            if (newId is not null) command.Parameters.AddWithValue("$new", newId);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new IOException("The registered repository no longer exists.");
            command.CommandText = newId is null ? "DELETE FROM user_credentials WHERE repository_id = $id;"
                : "UPDATE user_credentials SET repository_id = $new WHERE repository_id = $id;";
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqliteException ex) { throw new IOException("The repository and credential could not be updated atomically.", ex); }
    }

    private ApiTokenStoreResult Mutate(string repositoryId, string? newRepositoryId)
    {
        if (!RepositoryId.TryNormalize(repositoryId, out repositoryId)
            || (newRepositoryId is not null && !RepositoryId.TryNormalize(newRepositoryId, out newRepositoryId)))
            return ApiTokenStoreResult.Failure(ApiTokenStoreError.InvalidRepositoryId);
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = newRepositoryId is null
                ? "DELETE FROM user_credentials WHERE repository_id = $id;"
                : "UPDATE user_credentials SET repository_id = $new WHERE repository_id = $id;";
            command.Parameters.AddWithValue("$id", repositoryId);
            if (newRepositoryId is not null) command.Parameters.AddWithValue("$new", newRepositoryId);
            var count = command.ExecuteNonQuery();
            return newRepositoryId is not null && count == 0
                ? ApiTokenStoreResult.Failure(ApiTokenStoreError.TokenNotFound) : ApiTokenStoreResult.Success();
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            return ApiTokenStoreResult.Failure(ApiTokenStoreError.IoError);
        }
    }

    private byte[] Protect(ReadOnlySpan<char> token)
    {
        if (token.IsEmpty || token.Length > 2560) throw new IOException("Invalid credential length.");
        var plain = new byte[Encoding.UTF8.GetByteCount(token)];
        Encoding.UTF8.GetBytes(token, plain);
        try { return protector.Protect(plain, Entropy); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS user_credentials (repository_id TEXT NOT NULL PRIMARY KEY, protected_token BLOB NOT NULL);";
            command.ExecuteNonQuery();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }
}
