using System.Text.Json;
using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.Git;
using Githubie.Infrastructure.Configuration;
using Githubie.Infrastructure.Git;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Githubie.Server.Tests;

public sealed class UserHostGitCredentialWiringTests
{
    [Fact]
    public async Task UserHost_GitAskPassReadsTheSameCredentialDatabaseAsTheHost()
    {
        var root = Path.Combine(Path.GetTempPath(), "Githubie-user-git-credential-" + Guid.NewGuid().ToString("N"));
        var layout = GithubiePathLayout.FromUserProfileRoot(AppContext.BaseDirectory, root);
        try
        {
            await new UserProfileStore(layout).InitializeAsync(TestContext.Current.CancellationToken);
            var config = Path.Combine(layout.ConfigDirectory, "githubie.json");
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new { mcp_port = 45999, mcp_path = "/mcp", repositories = new { } }),
                TestContext.Current.CancellationToken);

            var result = await GithubieCompositionRoot.BuildForUserHostAsync(config, layout, false, false, TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
            await using var services = result.Services!;
            services.GetRequiredService<IGitCommandClient>().Should().BeOfType<GitCommandClient>()
                .Which.UserCredentialDatabasePath.Should().Be(layout.RepositoryDatabasePath);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
