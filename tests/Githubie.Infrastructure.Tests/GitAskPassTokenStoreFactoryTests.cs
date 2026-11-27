using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.Git;
using Githubie.Infrastructure.Configuration;
using Githubie.Infrastructure.Credentials;
using Xunit;

namespace Githubie.Infrastructure.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class GitAskPassTokenStoreFactoryTests
{
    private const string BinDirectory = @"C:\Githubie\bin";

    [Fact]
    public void Create_WithoutUserDatabase_UsesServiceSecretsDirectory()
    {
        GitAskPassTokenStoreFactory.Create(BinDirectory, null).Should().BeOfType<DpapiFileTokenStore>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"data\githubie.db")]
    [InlineData(@"C:\definitely-missing-githubie\data\githubie.db")]
    public void Create_InvalidUserDatabase_FailsWithoutFallingBackToServiceSecrets(string path)
    {
        GitAskPassTokenStoreFactory.Create(BinDirectory, path).Should().BeNull();
    }

    [Fact]
    public async Task Create_UserDatabase_ReturnsTokenSavedByUserHost()
    {
        var root = Path.Combine(Path.GetTempPath(), "Githubie-askpass-user-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "githubie.db");
            await new SqliteRepositoryConfigurationStore(path)
                .InitializeAsync(new Dictionary<string, RepositoryOptions>(), TestContext.Current.CancellationToken);
            new SqliteUserCredentialStore(path, new CurrentUserDpapiProtector()).Save("sample", "user-host-secret").IsSuccess.Should().BeTrue();

            var store = GitAskPassTokenStoreFactory.Create(BinDirectory, path);
            var response = new GitAskPassResponder(store!).Respond("sample", "Password for 'https://github.com': ");

            store.Should().BeOfType<SqliteUserCredentialStore>();
            response.IsSuccess.Should().BeTrue();
            response.Value.Should().Be("user-host-secret");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
