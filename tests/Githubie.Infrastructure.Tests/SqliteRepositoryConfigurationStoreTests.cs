using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Infrastructure.Configuration;
using Xunit;

namespace Githubie.Infrastructure.Tests;

public sealed class SqliteRepositoryConfigurationStoreTests
{
    [Fact]
    public async Task GetRepositoryAsync_UnregisteredId_ReturnsNull()
    {
        var root = CreateRoot();
        try
        {
            var store = new SqliteRepositoryConfigurationStore(Path.Combine(root, "repositories.db"));
            await store.InitializeAsync(
                new Dictionary<string, RepositoryOptions>(),
                TestContext.Current.CancellationToken);

            var options = await store.GetRepositoryAsync("missing", TestContext.Current.CancellationToken);

            Assert.Null(options);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task GetRepositoryAsync_RegisteredId_ReturnsRepositoryOptions()
    {
        var root = CreateRoot();
        var databasePath = Path.Combine(root, "githubie.db");
        try
        {
            var store = new SqliteRepositoryConfigurationStore(databasePath);
            await store.InitializeAsync(
                new Dictionary<string, RepositoryOptions>
                {
                    ["sample"] = CreateOptions("example-owner", "example-repo", "C:\\sample"),
                },
                TestContext.Current.CancellationToken);

            var options = await store.GetRepositoryAsync("sample", TestContext.Current.CancellationToken);

            options.Should().NotBeNull();
            options!.GitHubOwner.Should().Be("example-owner");
            options.GitHubRepo.Should().Be("example-repo");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task InitializeAsync_FirstRun_ImportsLegacyRepositoriesOnce()
    {
        var root = CreateRoot();
        var databasePath = Path.Combine(root, "githubie.db");
        try
        {
            var store = new SqliteRepositoryConfigurationStore(databasePath);
            var initial = await store.InitializeAsync(
                new Dictionary<string, RepositoryOptions>
                {
                    ["legacy"] = CreateOptions("owner", "legacy", "C:\\legacy"),
                },
                TestContext.Current.CancellationToken);

            await store.SaveRepositoryAsync(
                "added", CreateOptions("owner", "added", "C:\\added"), TestContext.Current.CancellationToken);
            var reloaded = await new SqliteRepositoryConfigurationStore(databasePath).InitializeAsync(
                new Dictionary<string, RepositoryOptions>
                {
                    ["later-json"] = CreateOptions("owner", "later", "C:\\later"),
                },
                TestContext.Current.CancellationToken);

            initial.Keys.Should().Equal("legacy");
            reloaded.Keys.Should().BeEquivalentTo("legacy", "added");
            reloaded.Should().NotContainKey("later-json");
            reloaded["legacy"].Workflows["release.yml"].Inputs["version"].Required.Should().BeTrue();
            File.Exists(databasePath).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LoadExistingAsync_ExistingDatabase_ReadsWithoutImportingLegacyRepositories()
    {
        var root = CreateRoot();
        var databasePath = Path.Combine(root, "githubie.db");
        try
        {
            var store = new SqliteRepositoryConfigurationStore(databasePath, busyTimeoutSeconds: 30);
            await store.InitializeAsync(
                new Dictionary<string, RepositoryOptions>
                {
                    ["existing"] = CreateOptions("owner", "existing", "C:\\existing"),
                },
                TestContext.Current.CancellationToken);

            var repositories = await store.LoadExistingAsync(TestContext.Current.CancellationToken);

            repositories.Keys.Should().Equal("existing");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SaveRepositoryAsync_TwoStoreInstances_PreserveIndependentWrites()
    {
        var root = CreateRoot();
        var databasePath = Path.Combine(root, "githubie.db");
        try
        {
            var first = new SqliteRepositoryConfigurationStore(databasePath);
            await first.InitializeAsync(
                new Dictionary<string, RepositoryOptions>(), TestContext.Current.CancellationToken);
            var second = new SqliteRepositoryConfigurationStore(databasePath);
            await second.InitializeAsync(
                new Dictionary<string, RepositoryOptions>(), TestContext.Current.CancellationToken);

            await first.SaveRepositoryAsync(
                "first", CreateOptions("owner", "first", "C:\\first"), TestContext.Current.CancellationToken);
            await second.SaveRepositoryAsync(
                "second", CreateOptions("owner", "second", "C:\\second"), TestContext.Current.CancellationToken);

            var reloaded = await new SqliteRepositoryConfigurationStore(databasePath).InitializeAsync(
                new Dictionary<string, RepositoryOptions>(), TestContext.Current.CancellationToken);
            reloaded.Keys.Should().BeEquivalentTo("first", "second");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task DeleteRepositoryAsync_RemovesOnlyRequestedEntry()
    {
        var root = CreateRoot();
        var databasePath = Path.Combine(root, "githubie.db");
        try
        {
            var store = new SqliteRepositoryConfigurationStore(databasePath);
            await store.InitializeAsync(
                new Dictionary<string, RepositoryOptions>
                {
                    ["keep"] = CreateOptions("owner", "keep", "C:\\keep"),
                    ["remove"] = CreateOptions("owner", "remove", "C:\\remove"),
                },
                TestContext.Current.CancellationToken);

            await store.DeleteRepositoryAsync("remove", TestContext.Current.CancellationToken);

            var reloaded = await new SqliteRepositoryConfigurationStore(databasePath).InitializeAsync(
                new Dictionary<string, RepositoryOptions>(), TestContext.Current.CancellationToken);
            reloaded.Keys.Should().Equal("keep");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RenameRepositoryAsync_TargetExists_DoesNotRemoveSource()
    {
        var root = CreateRoot();
        var databasePath = Path.Combine(root, "githubie.db");
        try
        {
            var store = new SqliteRepositoryConfigurationStore(databasePath);
            await store.InitializeAsync(
                new Dictionary<string, RepositoryOptions>
                {
                    ["source"] = CreateOptions("owner", "source", "C:\\source"),
                    ["target"] = CreateOptions("owner", "target", "C:\\target"),
                },
                TestContext.Current.CancellationToken);

            var action = () => store.RenameRepositoryAsync(
                "source", "target", TestContext.Current.CancellationToken);

            await action.Should().ThrowAsync<InvalidOperationException>();
            var reloaded = await new SqliteRepositoryConfigurationStore(databasePath).InitializeAsync(
                new Dictionary<string, RepositoryOptions>(), TestContext.Current.CancellationToken);
            reloaded.Keys.Should().BeEquivalentTo("source", "target");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"githubie-sqlite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static RepositoryOptions CreateOptions(string owner, string repo, string localRoot) => new(
        owner, repo, localRoot, "origin", "develop", "main",
        ["develop"], ["develop", "main"], ["main"], "main",
        "^v[0-9]+\\.[0-9]+\\.[0-9]+.*$", "merge", true)
    {
        Workflows = new Dictionary<string, WorkflowPolicyOptions>(StringComparer.Ordinal)
        {
            ["release.yml"] = new(
                ["main"],
                new Dictionary<string, WorkflowInputPolicyOptions>(StringComparer.Ordinal)
                {
                    ["version"] = new(Required: true, MaxLength: 32),
                }),
        },
    };
}
