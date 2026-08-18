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
        const string repositoryId = "sample-repo";
        const string token = "ghp_example_token_value_1234567890";

        var saveResult = store.Save(repositoryId, token);
        saveResult.IsSuccess.Should().BeTrue();

        var readResult = store.Read(repositoryId);
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

        var result = store.Read("never-saved");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(Githubie.Application.Credentials.ApiTokenStoreError.TokenNotFound);
    }

    public void Dispose()
    {
        if (Directory.Exists(_secretsDirectory))
        {
            Directory.Delete(_secretsDirectory, recursive: true);
        }
    }
}
