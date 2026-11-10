using FluentAssertions;
using Githubie.Application.GitHub;
using Githubie.Application.Configuration;
using Githubie.Application.Repositories;
using NSubstitute;
using Xunit;

namespace Githubie.Application.Tests;

public sealed partial class GitHubPullRequestLifecycleTests
{
    private static readonly string ExpectedSha = new('1', 40);
    private static readonly string MergedSha = new('2', 40);

    [Fact]
    public async Task MergeContract_NullStrategy_UsesRegisteredRepositoryDefault()
    {
        var repositories = new Dictionary<string, RepositoryOptions>
        {
            ["sample"] = new("owner", "repo", Path.GetTempPath(), "origin", "develop", "main",
                ["develop"], ["develop", "main"], ["main"], "main", "^v", "squash", true),
        };
        var gateway = new GitHubRepositoryGateway(new RepositoryAllowlist(repositories), _api, mergeabilityPollInterval: TimeSpan.Zero);
        _api.GetPullRequestAsync("sample", "owner", "repo", 1, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubPullRequestInfo>.Success(PullRequest(GitHubPullRequestState.Open)));
        _api.MergePullRequestAsync("sample", "owner", "repo", Arg.Any<GitHubPullRequestMerge>(), Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubPullRequestInfo>.Success(PullRequest(GitHubPullRequestState.Merged)));

        var result = await gateway.MergePullRequestAsync("sample", new(1, null, null, ExpectedSha), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        await _api.Received(1).MergePullRequestAsync("sample", "owner", "repo",
            Arg.Is<GitHubPullRequestMerge>(x => x.MergeMethod == GitHubMergeMethod.Squash), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, null, null, null, false, GitHubError.PullRequestNumberInvalid)]
    [InlineData(-1, null, null, null, false, GitHubError.PullRequestNumberInvalid)]
    [InlineData(1, 99, null, null, false, GitHubError.PullRequestMergeMethodInvalid)]
    [InlineData(1, null, " ", null, false, GitHubError.PullRequestMergeMessageInvalid)]
    [InlineData(1, null, "", null, false, GitHubError.PullRequestMergeMessageInvalid)]
    [InlineData(1, null, null, null, true, GitHubError.ExpectedSourceCommitRequired)]
    [InlineData(1, null, null, "abc", false, GitHubError.ExpectedSourceCommitInvalid)]
    [InlineData(1, null, null, "111111111111111111111111111111111111111g", false, GitHubError.ExpectedSourceCommitInvalid)]
    public async Task MergeContract_InvalidInput_StopsBeforeAnyApiCall(
        int number, int? method, string? message, string? sha, bool required, GitHubError expected)
    {
        var request = new GitHubPullRequestMerge(number, (GitHubMergeMethod?)method, message, sha)
        { RequireExpectedSourceCommit = required };

        var result = await _gateway.MergePullRequestAsync("sample", request, TestContext.Current.CancellationToken);

        result.Error.Should().Be(expected);
        _api.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("changed", GitHubError.ExpectedSourceCommitMismatch)]
    [InlineData("draft", GitHubError.PullRequestDraft)]
    [InlineData("closed", GitHubError.PullRequestNotOpen)]
    [InlineData("route", GitHubError.PullRequestRouteNotAllowed)]
    [InlineData("missing_sha", GitHubError.InvalidResponse)]
    [InlineData("missing_draft", GitHubError.InvalidResponse)]
    [InlineData("wrong_number", GitHubError.InvalidResponse)]
    public async Task MergeContract_RejectedSnapshot_NeverSendsPut(string kind, GitHubError expected)
    {
        var snapshot = PullRequest(GitHubPullRequestState.Open);
        snapshot = kind switch
        {
            "changed" => snapshot with { SourceCommitSha = new string('3', 40) },
            "draft" => snapshot with { Draft = true },
            "closed" => snapshot with { State = GitHubPullRequestState.Closed },
            "route" => snapshot with { Source = "unapproved" },
            "missing_sha" => snapshot with { SourceCommitSha = null },
            "missing_draft" => snapshot with { Draft = null },
            _ => snapshot with { Number = 99 },
        };
        _api.GetPullRequestAsync("sample", "owner", "repo", 1, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubPullRequestInfo>.Success(snapshot));

        var result = await _gateway.MergePullRequestAsync("sample",
            new(1, null, null, ExpectedSha) { RequireExpectedSourceCommit = true }, TestContext.Current.CancellationToken);

        result.Error.Should().Be(expected);
        await _api.DidNotReceive().MergePullRequestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<GitHubPullRequestMerge>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MergeContract_AlreadyMerged_ReturnsObservedSnapshotWithoutPut()
    {
        var snapshot = PullRequest(GitHubPullRequestState.Merged);
        _api.GetPullRequestAsync("sample", "owner", "repo", 1, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubPullRequestInfo>.Success(snapshot));

        var result = await _gateway.MergePullRequestAsync("sample", new(1, null, null, ExpectedSha), TestContext.Current.CancellationToken);

        result.Value.Should().BeSameAs(snapshot);
        result.Value!.SourceCommitSha.Should().Be(ExpectedSha);
        result.Value.MergeCommitSha.Should().Be(MergedSha);
        await _api.DidNotReceive().MergePullRequestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<GitHubPullRequestMerge>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MergeContract_ValidRequest_PreservesMessageAndDispatchesOnce()
    {
        const string message = "  approved body\nkeep spacing  ";
        _api.GetPullRequestAsync("sample", "owner", "repo", 1, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubPullRequestInfo>.Success(PullRequest(GitHubPullRequestState.Open)));
        _api.MergePullRequestAsync("sample", "owner", "repo", Arg.Any<GitHubPullRequestMerge>(), Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubPullRequestInfo>.Success(PullRequest(GitHubPullRequestState.Merged)));

        var result = await _gateway.MergePullRequestAsync("sample", new(1, GitHubMergeMethod.Squash, message, ExpectedSha), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        await _api.Received(1).MergePullRequestAsync("sample", "owner", "repo",
            Arg.Is<GitHubPullRequestMerge>(x => x.ExpectedSourceCommitSha == ExpectedSha && x.CommitMessage == message && x.MergeMethod == GitHubMergeMethod.Squash), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(GitHubError.NetworkError)]
    [InlineData(GitHubError.Timeout)]
    [InlineData(GitHubError.InvalidResponse)]
    public async Task MergeContract_DispatchedAmbiguousFailure_IsUnknownWithoutReplay(GitHubError error)
    {
        _api.GetPullRequestAsync("sample", "owner", "repo", 1, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubPullRequestInfo>.Success(PullRequest(GitHubPullRequestState.Open)));
        _api.MergePullRequestAsync("sample", "owner", "repo", Arg.Any<GitHubPullRequestMerge>(), Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubPullRequestInfo>.Failure(error));

        var result = await _gateway.MergePullRequestAsync("sample", new(1, null, null, ExpectedSha), TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitHubError.PullRequestMergeResultUnknown);
        await _api.Received(1).MergePullRequestAsync("sample", "owner", "repo", Arg.Any<GitHubPullRequestMerge>(), Arg.Any<CancellationToken>());
        await _api.Received(1).GetPullRequestAsync("sample", "owner", "repo", 1, Arg.Any<CancellationToken>());
    }
}
