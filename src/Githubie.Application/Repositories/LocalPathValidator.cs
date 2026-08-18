using Githubie.Application.Configuration;

namespace Githubie.Application.Repositories;

/// <summary>
/// 設定済みLocalRootとローカルGitリポジトリの実体が一致することを検証します。
/// `..`によるroot外参照、symlink/junctionによるroot外参照、未登録Repositoryを拒否します。
/// </summary>
public sealed class LocalPathValidator(IRepositoryEnvironment environment)
{
    private readonly IRepositoryEnvironment _environment = environment;

    public RepositoryValidationResult Validate(RepositoryOptions options)
    {
        var configuredFullPath = _environment.GetFullPath(options.LocalRoot);

        if (!string.Equals(configuredFullPath, Path.TrimEndingDirectorySeparator(configuredFullPath), StringComparison.Ordinal))
        {
            configuredFullPath = Path.TrimEndingDirectorySeparator(configuredFullPath);
        }

        if (!_environment.DirectoryExists(configuredFullPath))
        {
            return RepositoryValidationResult.Denied(RepositoryValidationError.LocalRootNotFound);
        }

        if (!_environment.GitMetadataExists(configuredFullPath))
        {
            return RepositoryValidationResult.Denied(RepositoryValidationError.GitMetadataNotFound);
        }

        if (_environment.ContainsReparsePoint(configuredFullPath))
        {
            return RepositoryValidationResult.Denied(RepositoryValidationError.ReparsePointDetected);
        }

        return RepositoryValidationResult.Allowed();
    }
}
