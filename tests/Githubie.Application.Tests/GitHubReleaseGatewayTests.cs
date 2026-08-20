using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.GitHub;
using Githubie.Application.Repositories;
using NSubstitute;
using Xunit;

namespace Githubie.Application.Tests;

public sealed class GitHubReleaseGatewayTests
{
    private const string Repository = "sample";
    private const string Sha = "1111111111111111111111111111111111111111";
    private readonly IGitHubApiClient _api = Substitute.For<IGitHubApiClient>();
    private readonly GitHubRepositoryGateway _gateway;

    public GitHubReleaseGatewayTests()
    {
        var options = new RepositoryOptions(
            "owner", "repo", "C:\\repo", "origin", "develop", "main", ["develop"], ["develop", "main"], ["main"],
            "main", "^v[0-9]+\\.[0-9]+\\.[0-9]+\\.[0-9]+$", "merge", true);
        _gateway = new GitHubRepositoryGateway(new RepositoryAllowlist(new Dictionary<string, RepositoryOptions> { [Repository] = options }), _api);
    }

    [Fact]
    public async Task CreateReleaseAsync_TagTargetsCurrentMain_DelegatesToApi()
    {
        var request = new GitHubReleaseCreate("v1.2.0.0", "Release", null, false, false, ["C:\\repo\\a.zip"]);
        _api.GetTagAsync(Repository, "owner", "repo", request.Tag, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubTagInfo>.Success(new(request.Tag, Sha, null, null, null)));
        _api.GetBranchAsync(Repository, "owner", "repo", "main", Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubBranchInfo>.Success(new("main", Sha, true)));
        _api.CreateReleaseAsync(Repository, "owner", "repo", "C:\\repo", request, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubReleaseInfo>.Success(new(1, request.Tag, request.Name, false, false, "https://example.com", [])));

        var result = await _gateway.CreateReleaseAsync(Repository, request, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        await _api.Received(1).CreateReleaseAsync(Repository, "owner", "repo", "C:\\repo", request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateReleaseAsync_TagDoesNotTargetCurrentMain_RejectsBeforeCreate()
    {
        var request = new GitHubReleaseCreate("v1.2.0.0", "Release", null, false, false, ["C:\\repo\\a.zip"]);
        _api.GetTagAsync(Repository, "owner", "repo", request.Tag, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubTagInfo>.Success(new(request.Tag, Sha, null, null, null)));
        _api.GetBranchAsync(Repository, "owner", "repo", "main", Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubBranchInfo>.Success(new("main", new string('2', 40), true)));

        var result = await _gateway.CreateReleaseAsync(Repository, request, TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitHubError.TagTargetNotAllowed);
        await _api.DidNotReceive().CreateReleaseAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<GitHubReleaseCreate>(), Arg.Any<CancellationToken>());
    }
}
