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
        _gateway = new GitHubRepositoryGateway(allowlist, _api, mergeabilityPollInterval: TimeSpan.Zero);
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

    [Fact]
    public async Task ApprovePullRequestAsync_OpenPullRequest_CreatesApproval()
    {
        _api.GetPullRequestAsync("sample", "owner", "repo", 1, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubPullRequestInfo>.Success(PullRequest(GitHubPullRequestState.Open)));
        _api.CreatePullRequestReviewAsync(
                "sample", "owner", "repo", 1, GitHubPullRequestReviewAction.Approve, "Looks good",
                Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubPullRequestReview>.Success(Review("APPROVED")));

        var result = await _gateway.ApprovePullRequestAsync(
            "sample", 1, "Looks good", TestContext.Current.CancellationToken);

        result.Value!.State.Should().Be("APPROVED");
    }

    [Fact]
    public async Task RequestPullRequestChangesAsync_EmptyBody_ReturnsValidationError()
    {
        var result = await _gateway.RequestPullRequestChangesAsync(
            "sample", 1, " ", TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitHubError.PullRequestReviewInvalid);
        await _api.DidNotReceive().CreatePullRequestReviewAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<GitHubPullRequestReviewAction>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApprovePullRequestAsync_ClosedPullRequest_ReturnsNotOpen()
    {
        _api.GetPullRequestAsync("sample", "owner", "repo", 1, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubPullRequestInfo>.Success(PullRequest(GitHubPullRequestState.Closed)));

        var result = await _gateway.ApprovePullRequestAsync(
            "sample", 1, null, TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitHubError.PullRequestNotOpen);
    }

    [Fact]
    public async Task MergePullRequestAsync_CalculatingThenMergeable_PollsAndMerges()
    {
        _api.GetPullRequestAsync("sample", "owner", "repo", 1, Arg.Any<CancellationToken>())
            .Returns(
                GitHubResult<GitHubPullRequestInfo>.Success(PullRequest(
                    GitHubPullRequestState.Open, GitHubMergeabilityStatus.CalculatingRetryable, null)),
                GitHubResult<GitHubPullRequestInfo>.Success(PullRequest(
                    GitHubPullRequestState.Open, GitHubMergeabilityStatus.Mergeable, true)));
        _api.MergePullRequestAsync("sample", "owner", "repo", Arg.Any<GitHubPullRequestMerge>(), Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubPullRequestInfo>.Success(PullRequest(
                GitHubPullRequestState.Merged, GitHubMergeabilityStatus.Mergeable, true)));

        var result = await _gateway.MergePullRequestAsync(
            "sample", new GitHubPullRequestMerge(1, null, null), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        await _api.Received(2).GetPullRequestAsync(
            "sample", "owner", "repo", 1, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(GitHubMergeabilityStatus.Conflicting, GitHubError.PullRequestNotMergeable)]
    [InlineData(GitHubMergeabilityStatus.Blocked, GitHubError.PullRequestBlocked)]
    [InlineData(GitHubMergeabilityStatus.CalculatingRetryable, GitHubError.MergeabilityCalculating)]
    [InlineData(GitHubMergeabilityStatus.UnknownRetryable, GitHubError.MergeabilityUnknownRetryable)]
    public async Task MergePullRequestAsync_MergeabilityState_ReturnsMatchingError(string status, GitHubError expected)
    {
        _api.GetPullRequestAsync("sample", "owner", "repo", 1, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubPullRequestInfo>.Success(PullRequest(
                GitHubPullRequestState.Open, status, status == GitHubMergeabilityStatus.Mergeable)));

        var result = await _gateway.MergePullRequestAsync(
            "sample", new GitHubPullRequestMerge(1, null, null), TestContext.Current.CancellationToken);

        result.Error.Should().Be(expected);
        await _api.DidNotReceive().MergePullRequestAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<GitHubPullRequestMerge>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MergePullRequestAsync_ApiTemporaryFailure_RefreshesAndReturnsCalculating()
    {
        _api.GetPullRequestAsync("sample", "owner", "repo", 1, Arg.Any<CancellationToken>())
            .Returns(
                GitHubResult<GitHubPullRequestInfo>.Success(PullRequest(
                    GitHubPullRequestState.Open, GitHubMergeabilityStatus.Mergeable, true)),
                GitHubResult<GitHubPullRequestInfo>.Success(PullRequest(
                    GitHubPullRequestState.Open, GitHubMergeabilityStatus.CalculatingRetryable, null)));
        _api.MergePullRequestAsync("sample", "owner", "repo", Arg.Any<GitHubPullRequestMerge>(), Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubPullRequestInfo>.Failure(GitHubError.PullRequestNotMergeable));

        var result = await _gateway.MergePullRequestAsync(
            "sample", new GitHubPullRequestMerge(1, null, null), TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitHubError.MergeabilityCalculating);
    }

    private static GitHubPullRequestInfo PullRequest(
        GitHubPullRequestState state,
        string mergeabilityStatus = GitHubMergeabilityStatus.Mergeable,
        bool? mergeable = true) => new(
        1, "title", null, state, "develop", "main", "user", null, mergeable,
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "https://example.com/pr/1",
        mergeabilityStatus, mergeabilityStatus.EndsWith("_retryable", StringComparison.Ordinal) ? 2 : null);

    private static GitHubPullRequestReview Review(string state) => new(
        10, "body", "reviewer", state, DateTimeOffset.UnixEpoch, "abc", "https://example.com/review/10");
}
