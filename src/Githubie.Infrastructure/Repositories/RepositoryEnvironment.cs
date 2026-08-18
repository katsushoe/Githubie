using Githubie.Application.Repositories;

namespace Githubie.Infrastructure.Repositories;

/// <summary>
/// <see cref="IRepositoryEnvironment"/>の実ファイルシステム実装です。
/// </summary>
public sealed class RepositoryEnvironment : IRepositoryEnvironment
{
    public string GetFullPath(string path) => Path.GetFullPath(path);

    public bool DirectoryExists(string fullPath) => Directory.Exists(fullPath);

    public bool GitMetadataExists(string repositoryRootFullPath) =>
        Directory.Exists(Path.Combine(repositoryRootFullPath, ".git")) || File.Exists(Path.Combine(repositoryRootFullPath, ".git"));

    public bool ContainsReparsePoint(string repositoryRootFullPath)
    {
        var current = new DirectoryInfo(repositoryRootFullPath);
        while (current is not null)
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }
}
