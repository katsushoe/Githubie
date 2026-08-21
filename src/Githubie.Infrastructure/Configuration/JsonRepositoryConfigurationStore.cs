using Githubie.Application.Configuration;
using Githubie.Application.Repositories;

namespace Githubie.Infrastructure.Configuration;

/// <summary>githubie.jsonへRepository設定を原子的に保存します。</summary>
public sealed class JsonRepositoryConfigurationStore(
    string configPath,
    GithubieOptions initialOptions,
    IGithubieOptionsLoader loader) : IRepositoryConfigurationStore
{
    private GithubieOptions _options = initialOptions;

    public async Task SaveRepositoryAsync(
        string repositoryId,
        RepositoryOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryId);
        ArgumentNullException.ThrowIfNull(options);

        var repositories = new Dictionary<string, RepositoryOptions>(_options.Repositories, StringComparer.Ordinal)
        {
            [repositoryId] = options,
        };
        var updated = _options with { Repositories = repositories };
        var directory = Path.GetDirectoryName(configPath)
            ?? throw new IOException("Configuration directory could not be resolved.");
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(configPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await loader.SaveAsync(updated, stream, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, configPath, true);
            _options = updated;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public async Task DeleteRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryId);
        var repositories = new Dictionary<string, RepositoryOptions>(_options.Repositories, StringComparer.Ordinal);
        if (!repositories.Remove(repositoryId)) throw new KeyNotFoundException(repositoryId);
        await PersistAsync(_options with { Repositories = repositories }, cancellationToken);
    }

    public async Task RenameRepositoryAsync(
        string oldRepositoryId, string newRepositoryId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldRepositoryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newRepositoryId);
        var repositories = new Dictionary<string, RepositoryOptions>(_options.Repositories, StringComparer.Ordinal);
        if (!repositories.Remove(oldRepositoryId, out var options)) throw new KeyNotFoundException(oldRepositoryId);
        if (!repositories.TryAdd(newRepositoryId, options)) throw new InvalidOperationException(newRepositoryId);
        await PersistAsync(_options with { Repositories = repositories }, cancellationToken);
    }

    private async Task PersistAsync(GithubieOptions updated, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(configPath)
            ?? throw new IOException("Configuration directory could not be resolved.");
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(configPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await loader.SaveAsync(updated, stream, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, configPath, true);
            _options = updated;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
