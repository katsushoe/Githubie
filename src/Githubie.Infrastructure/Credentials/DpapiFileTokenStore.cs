using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Githubie.Application.Credentials;

namespace Githubie.Infrastructure.Credentials;

/// <summary>
/// Personal Access TokenをDPAPI(LocalMachineスコープ)で暗号化し、
/// `{secretsDirectory}/{repositoryId}.token`へ1ファイル/Repositoryで保存します。
/// トークンをbuckettie.json相当の設定ファイルへ平文保存しないための実装です。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiFileTokenStore : IApiTokenStore
{
    private const int MaxTokenLength = 2560;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Githubie/GitHub/DPAPI/v1");

    private readonly string _secretsDirectory;
    private readonly IDpapiProtector _protector;

    public DpapiFileTokenStore(string secretsDirectory, IDpapiProtector? protector = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretsDirectory);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DpapiFileTokenStore requires Windows.");
        }

        _secretsDirectory = secretsDirectory;
        _protector = protector ?? new DpapiProtector();
    }

    public ApiTokenStoreResult Save(string repositoryId, ReadOnlySpan<char> token)
    {
        if (!Application.Repositories.RepositoryId.IsValid(repositoryId))
        {
            return ApiTokenStoreResult.Failure(ApiTokenStoreError.InvalidRepositoryId);
        }

        if (token.IsEmpty)
        {
            return ApiTokenStoreResult.Failure(ApiTokenStoreError.TokenEmpty);
        }

        if (token.Length > MaxTokenLength)
        {
            return ApiTokenStoreResult.Failure(ApiTokenStoreError.TokenTooLong);
        }

        var plainBytes = Encoding.UTF8.GetBytes(token.ToString());
        try
        {
            WindowsSecretDirectorySecurity.Ensure(_secretsDirectory);

            var protectedBytes = _protector.Protect(plainBytes, Entropy);
            var path = GetTokenPath(repositoryId);
            var tempPath = path + ".tmp";

            File.WriteAllBytes(tempPath, protectedBytes);
            File.Move(tempPath, path, overwrite: true);

            return ApiTokenStoreResult.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ApiTokenStoreResult.Failure(ApiTokenStoreError.IoError);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    public ApiTokenStoreReadResult Read(string repositoryId)
    {
        if (!Application.Repositories.RepositoryId.IsValid(repositoryId))
        {
            return ApiTokenStoreReadResult.Failure(ApiTokenStoreError.InvalidRepositoryId);
        }

        var path = GetTokenPath(repositoryId);
        if (!File.Exists(path))
        {
            return ApiTokenStoreReadResult.Failure(ApiTokenStoreError.TokenNotFound);
        }

        byte[] protectedBytes;
        byte[]? plainBytes = null;
        try
        {
            protectedBytes = File.ReadAllBytes(path);
            plainBytes = _protector.Unprotect(protectedBytes, Entropy);
            return ApiTokenStoreReadResult.Success(Encoding.UTF8.GetChars(plainBytes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return ApiTokenStoreReadResult.Failure(ApiTokenStoreError.IoError);
        }
        finally
        {
            if (plainBytes is not null)
            {
                CryptographicOperations.ZeroMemory(plainBytes);
            }
        }
    }

    public ApiTokenStoreResult Delete(string repositoryId)
    {
        if (!Application.Repositories.RepositoryId.IsValid(repositoryId))
        {
            return ApiTokenStoreResult.Failure(ApiTokenStoreError.InvalidRepositoryId);
        }

        try
        {
            var path = GetTokenPath(repositoryId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return ApiTokenStoreResult.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ApiTokenStoreResult.Failure(ApiTokenStoreError.IoError);
        }
    }

    public ApiTokenStoreResult Rename(string oldRepositoryId, string newRepositoryId)
    {
        if (!Application.Repositories.RepositoryId.IsValid(oldRepositoryId)
            || !Application.Repositories.RepositoryId.IsValid(newRepositoryId))
            return ApiTokenStoreResult.Failure(ApiTokenStoreError.InvalidRepositoryId);

        var oldPath = GetTokenPath(oldRepositoryId);
        var newPath = GetTokenPath(newRepositoryId);
        if (!File.Exists(oldPath)) return ApiTokenStoreResult.Failure(ApiTokenStoreError.TokenNotFound);
        if (File.Exists(newPath)) return ApiTokenStoreResult.Failure(ApiTokenStoreError.IoError);
        try
        {
            File.Move(oldPath, newPath);
            return ApiTokenStoreResult.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ApiTokenStoreResult.Failure(ApiTokenStoreError.IoError);
        }
    }

    private string GetTokenPath(string repositoryId) => Path.Combine(_secretsDirectory, $"{repositoryId}.token");
}
