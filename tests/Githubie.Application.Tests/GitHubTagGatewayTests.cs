using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.GitHub;
using Githubie.Application.Repositories;
using NSubstitute;
using Xunit;

namespace Githubie.Application.Tests;

public sealed class GitHubTagGatewayTests
{
    private const string Repository = "sample";
    private const string Sha = "1111111111111111111111111111111111111111";
    private readonly IGitHubApiClient _api = Substitute.For<IGitHubApiClient>();
    private readonly GitHubRepositoryGateway _gateway;

    public GitHubTagGatewayTests()
    {
        var options = new RepositoryOptions(
            "owner", "repo", "C:\\repo", "origin", "develop", "main", ["develop"], ["develop", "main"], ["main"],
            "main", "^v[0-9]+\\.[0-9]+\\.[0-9]+\\.[0-9]+$", "merge", true);
        _gateway = new GitHubRepositoryGateway(
            new RepositoryAllowlist(new Dictionary<string, RepositoryOptions> { [Repository] = options }), _api);
    }

    [Fact]
    public async Task CreateTagAsync_FullCommitSha_UsesExactCommit()
    {
        _api.GetCommitShaAsync(Repository, "owner", "repo", Sha, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<string>.Success(Sha));
        _api.CreateTagAsync(Repository, "owner", "repo", Arg.Any<GitHubTagCreate>(), Arg.Any<CancellationToken>())
            .Returns(call => GitHubResult<GitHubTagInfo>.Success(new("v1.2.3.4", call.Arg<GitHubTagCreate>().TargetCommitSha, null, null, null)));

        var result = await _gateway.CreateTagAsync(Repository, "v1.2.3.4", Sha, null, TestContext.Current.CancellationToken);

        result.Value!.TargetCommitSha.Should().Be(Sha);
        await _api.Received(1).CreateTagAsync(
            Repository, "owner", "repo", Arg.Is<GitHubTagCreate>(request => request.TargetCommitSha == Sha), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateTagAsync_ExplicitBranch_UsesBranchHead()
    {
        _api.GetBranchAsync(Repository, "owner", "repo", "develop", Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubBranchInfo>.Success(new("develop", Sha, false)));
        _api.CreateTagAsync(Repository, "owner", "repo", Arg.Any<GitHubTagCreate>(), Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubTagInfo>.Success(new("v1.2.3.4", Sha, null, null, null)));

        var result = await _gateway.CreateTagAsync(Repository, "v1.2.3.4", "develop", null, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        await _api.Received(1).CreateTagAsync(
            Repository, "owner", "repo", Arg.Is<GitHubTagCreate>(request => request.TargetCommitSha == Sha), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("HEAD~1")]
    [InlineData("main^{commit}")]
    public async Task CreateTagAsync_InvalidSource_RejectsWithoutCreating(string source)
    {
        var result = await _gateway.CreateTagAsync(Repository, "v1.2.3.4", source, null, TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitHubError.TagSourceInvalid);
        await _api.DidNotReceive().CreateTagAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<GitHubTagCreate>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("2222222222222222222222222222222222222222")]
    public async Task CreateTagAsync_MissingSource_RejectsWithoutFallback(string source)
    {
        _api.GetBranchAsync(Repository, "owner", "repo", source, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubBranchInfo>.Failure(GitHubError.BranchNotFound));
        _api.GetCommitShaAsync(Repository, "owner", "repo", source, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<string>.Failure(GitHubError.BranchSourceNotFound));

        var result = await _gateway.CreateTagAsync(Repository, "v1.2.3.4", source, null, TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitHubError.TagSourceNotFound);
        await _api.DidNotReceive().CreateTagAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<GitHubTagCreate>(), Arg.Any<CancellationToken>());
        await _api.DidNotReceive().GetBranchAsync(Repository, "owner", "repo", "main", Arg.Any<CancellationToken>());
    }
}
