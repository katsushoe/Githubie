using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.GitHub;
using Githubie.Application.Repositories;
using NSubstitute;
using Xunit;

namespace Githubie.Application.Tests;

public sealed class GitHubRepositoryDescriptionGatewayTests
{
    private readonly IGitHubApiClient _api = Substitute.For<IGitHubApiClient>();
    private readonly GitHubRepositoryGateway _gateway;

    public GitHubRepositoryDescriptionGatewayTests()
    {
        var options = new RepositoryOptions(
            "owner", "repo", "C:\\repo", "origin", "develop", "main", ["develop"], ["develop", "main"], ["main"],
            "main", "^v[0-9]+\\.[0-9]+\\.[0-9]+$", "merge", true);
        _gateway = new GitHubRepositoryGateway(
            new RepositoryAllowlist(new Dictionary<string, RepositoryOptions> { ["sample"] = options }), _api);
    }

    [Theory]
    [InlineData(350, true)]
    [InlineData(351, false)]
    public async Task UpdateRepositoryDescriptionAsync_ValidatesMaximumLength(int length, bool expectedSuccess)
    {
        var description = new string('あ', length);
        _api.UpdateRepositoryDescriptionAsync("sample", "owner", "repo", description, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubRepositoryInfo>.Success(new("owner", "repo", "main", description)));
        var result = await _gateway.UpdateRepositoryDescriptionAsync("sample", description, TestContext.Current.CancellationToken);
        result.IsSuccess.Should().Be(expectedSuccess);
        if (!expectedSuccess) result.Error.Should().Be(GitHubError.RepositoryDescriptionInvalid);
    }

    [Fact]
    public async Task UpdateRepositoryDescriptionAsync_AllowsEmptyDescription()
    {
        _api.UpdateRepositoryDescriptionAsync("sample", "owner", "repo", string.Empty, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubRepositoryInfo>.Success(new("owner", "repo", "main", string.Empty)));
        var result = await _gateway.UpdateRepositoryDescriptionAsync("sample", string.Empty, TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue();
    }
}
