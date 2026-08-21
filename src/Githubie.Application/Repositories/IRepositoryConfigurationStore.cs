using Githubie.Application.Configuration;

namespace Githubie.Application.Repositories;

/// <summary>Repository設定を永続化します。</summary>
public interface IRepositoryConfigurationStore
{
    Task SaveRepositoryAsync(
        string repositoryId,
        RepositoryOptions options,
        CancellationToken cancellationToken);

    Task DeleteRepositoryAsync(string repositoryId, CancellationToken cancellationToken);
    Task RenameRepositoryAsync(string oldRepositoryId, string newRepositoryId, CancellationToken cancellationToken);
}
