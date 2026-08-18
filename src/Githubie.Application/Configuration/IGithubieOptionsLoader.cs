namespace Githubie.Application.Configuration;

/// <summary>
/// githubie.jsonの読み書きを行うポートです。
/// </summary>
public interface IGithubieOptionsLoader
{
    Task<ConfigurationLoadResult> LoadAsync(Stream stream, CancellationToken cancellationToken);

    Task SaveAsync(GithubieOptions options, Stream stream, CancellationToken cancellationToken);
}
