using System.Collections.Concurrent;
using Githubie.Application.Configuration;

namespace Githubie.Application.Repositories;

/// <summary>
/// 設定ファイルに登録済みのRepositoryだけを解決するAllowlistです。
/// Agentは`repository`という内部IDだけを指定でき、owner/repo/localRootを自由指定できません。
/// </summary>
public sealed class RepositoryAllowlist
{
    private readonly ConcurrentDictionary<string, RepositoryOptions> _repositories;

    public RepositoryAllowlist(IReadOnlyDictionary<string, RepositoryOptions> repositories)
    {
        _repositories = new ConcurrentDictionary<string, RepositoryOptions>(repositories, StringComparer.Ordinal);
    }

    public bool TryGet(string repositoryId, out RepositoryOptions options)
    {
        if (string.IsNullOrWhiteSpace(repositoryId))
        {
            options = null!;
            return false;
        }

        return _repositories.TryGetValue(repositoryId, out options!);
    }

    public bool TryAdd(string repositoryId, RepositoryOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryId);
        ArgumentNullException.ThrowIfNull(options);
        return _repositories.TryAdd(repositoryId, options);
    }

    public IReadOnlyCollection<string> RepositoryIds => (IReadOnlyCollection<string>)_repositories.Keys;
}
