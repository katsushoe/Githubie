using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.GitHub;
using Githubie.Application.Repositories;
using NSubstitute;
using Xunit;

namespace Githubie.Application.Tests;

public sealed class GitHubPullRequestLifecycleTests
{
    private readonly IGitHubApiClient _api = Substitute.For<IGitHubApiClient>();
    private readonly GitHubRepositoryGateway _gateway;

    public GitHubPullRequestLifecycleTests()
    {
        var allowlist = new RepositoryAllowlist(new Dictionary<string, RepositoryOptions>
        {
            ["sample"] = new("owner", "repo", "C:\\repo", "origin", "develop", "main",
                ["develop"], ["develop", "main"], ["main"], "main", "^v", "merge", true),
        });
        _gateway = new GitHubRepositoryGateway(allowlist, _api);
    }

    [Fact]
    public async Task ClosePullRequestAsync_OpenPullRequest_UpdatesState()
    {
        _api.GetPullRequestAsync("sample", "owner", "repo", 1, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubPullRequestInfo>.Success(PullRequest(GitHubPullRequestState.Open)));
        _api.UpdatePullRequestStateAsync("sample", "owner", "repo", 1, GitHubPullRequestState.Closed, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubPullRequestInfo>.Success(PullRequest(GitHubPullRequestState.Closed)));

        var result = await _gateway.ClosePullRequestAsync("sample", 1, TestContext.Current.CancellationToken);

        result.Value!.State.Should().Be(GitHubPullRequestState.Closed);
    }

    [Fact]
    public async Task ReopenPullRequestAsync_MergedPullRequest_ReturnsStateError()
    {
        _api.GetPullRequestAsync("sample", "owner", "repo", 1, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubPullRequestInfo>.Success(PullRequest(GitHubPullRequestState.Merged)));

        var result = await _gateway.ReopenPullRequestAsync("sample", 1, TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitHubError.PullRequestStateNotAllowed);
        await _api.DidNotReceive().UpdatePullRequestStateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<GitHubPullRequestState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreatePullRequestCommentAsync_EmptyBody_ReturnsValidationError()
    {
        var result = await _gateway.CreatePullRequestCommentAsync(
            "sample", 1, " ", TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitHubError.PullRequestCommentInvalid);
        await _api.DidNotReceive().CreatePullRequestCommentAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static GitHubPullRequestInfo PullRequest(GitHubPullRequestState state) => new(
        1, "title", null, state, "develop", "main", "user", null, true,
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "https://example.com/pr/1");
}
