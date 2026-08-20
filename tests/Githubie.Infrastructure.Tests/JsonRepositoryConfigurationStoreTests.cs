using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Infrastructure.Configuration;
using Xunit;

namespace Githubie.Infrastructure.Tests;

public sealed class JsonRepositoryConfigurationStoreTests
{
    [Fact]
    public async Task SaveRepositoryAsync_WritesStrictJsonAndPreservesExistingEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), $"githubie-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "githubie.json");
        var existing = new GithubieOptions(45460, "/mcp", new Dictionary<string, RepositoryOptions>
        {
            ["existing"] = CreateOptions("owner", "existing", "C:\\existing"),
        });
        var loader = new JsonGithubieOptionsLoader();
        try
        {
            await using (var initial = File.Create(configPath))
                await loader.SaveAsync(existing, initial, TestContext.Current.CancellationToken);
            var store = new JsonRepositoryConfigurationStore(configPath, existing, loader);

            await store.SaveRepositoryAsync(
                "added", CreateOptions("derived", "added", "C:\\added"), TestContext.Current.CancellationToken);

            await using var saved = File.OpenRead(configPath);
            var result = await loader.LoadAsync(saved, TestContext.Current.CancellationToken);
            result.IsSuccess.Should().BeTrue();
            result.Options!.Repositories.Keys.Should().BeEquivalentTo("existing", "added");
            result.Options.Repositories["added"].GitHubOwner.Should().Be("derived");
            Directory.GetFiles(root, "*.tmp").Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task DeleteRepositoryAsync_RemovesOnlyRequestedEntry()
    {
        var root = Path.Combine(Path.GetTempPath(), $"githubie-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "githubie.json");
        var existing = new GithubieOptions(45460, "/mcp", new Dictionary<string, RepositoryOptions>
        {
            ["keep"] = CreateOptions("owner", "keep", "C:\\keep"),
            ["remove"] = CreateOptions("owner", "remove", "C:\\remove"),
        });
        var loader = new JsonGithubieOptionsLoader();
        try
        {
            await using (var initial = File.Create(configPath))
                await loader.SaveAsync(existing, initial, TestContext.Current.CancellationToken);
            var store = new JsonRepositoryConfigurationStore(configPath, existing, loader);

            await store.DeleteRepositoryAsync("remove", TestContext.Current.CancellationToken);

            await using var saved = File.OpenRead(configPath);
            var result = await loader.LoadAsync(saved, TestContext.Current.CancellationToken);
            result.IsSuccess.Should().BeTrue();
            result.Options!.Repositories.Keys.Should().Equal("keep");
            Directory.GetFiles(root, "*.tmp").Should().BeEmpty();
        }
        finally { Directory.Delete(root, true); }
    }

    private static RepositoryOptions CreateOptions(string owner, string repo, string localRoot) => new(
        owner, repo, localRoot, "origin", "develop", "main",
        ["develop"], ["develop", "main"], ["main"], "main",
        "^v[0-9]+\\.[0-9]+\\.[0-9]+.*$", "merge", true);
}
