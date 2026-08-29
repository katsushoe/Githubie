using System.Runtime.Versioning;
using FluentAssertions;
using Githubie.Infrastructure.Credentials;
using Xunit;

namespace Githubie.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public sealed class DpapiFileTokenStoreTests : IDisposable
{
    private readonly string _secretsDirectory = Path.Combine(Path.GetTempPath(), $"githubie-token-test-{Guid.NewGuid():N}");

    [Fact]
    public void SaveReadDelete_RoundTripsThroughRealDpapiAndAcl()
    {
        var store = new DpapiFileTokenStore(_secretsDirectory);
        const string repositoryId = "samplerepo";
        const string token = "ghp_example_token_value_1234567890";

        var saveResult = store.Save(repositoryId, token);
        saveResult.IsSuccess.Should().BeTrue();

        var readResult = store.Read("SAMPLEREPO");
        readResult.IsSuccess.Should().BeTrue();
        new string(readResult.Token!).Should().Be(token);

        var deleteResult = store.Delete(repositoryId);
        deleteResult.IsSuccess.Should().BeTrue();

        var readAfterDelete = store.Read(repositoryId);
        readAfterDelete.IsSuccess.Should().BeFalse();
        readAfterDelete.Error.Should().Be(Githubie.Application.Credentials.ApiTokenStoreError.TokenNotFound);
    }

    [Fact]
    public void Read_UnknownRepository_ReturnsTokenNotFound()
    {
        var store = new DpapiFileTokenStore(_secretsDirectory);

        var result = store.Read("neversaved");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(Githubie.Application.Credentials.ApiTokenStoreError.TokenNotFound);
    }

    [Fact]
    public void Rename_ExistingToken_MovesEncryptedCredentialWithoutLeavingOldId()
    {
        var store = new DpapiFileTokenStore(_secretsDirectory);
        store.Save("oldid", "ghp_example_token_value").IsSuccess.Should().BeTrue();

        var result = store.Rename("oldid", "newid");

        result.IsSuccess.Should().BeTrue();
        store.Read("oldid").Error.Should().Be(Githubie.Application.Credentials.ApiTokenStoreError.TokenNotFound);
        new string(store.Read("newid").Token!).Should().Be("ghp_example_token_value");
    }

    public void Dispose()
    {
        if (Directory.Exists(_secretsDirectory))
        {
            Directory.Delete(_secretsDirectory, recursive: true);
        }
    }
}
