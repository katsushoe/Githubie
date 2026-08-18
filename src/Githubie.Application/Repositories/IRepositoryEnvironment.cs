namespace Githubie.Application.Repositories;

/// <summary>
/// OS依存のファイルシステム確認をポートとして分離します。
/// </summary>
public interface IRepositoryEnvironment
{
    string GetFullPath(string path);

    bool DirectoryExists(string fullPath);

    bool GitMetadataExists(string repositoryRootFullPath);

    bool ContainsReparsePoint(string repositoryRootFullPath);
}
