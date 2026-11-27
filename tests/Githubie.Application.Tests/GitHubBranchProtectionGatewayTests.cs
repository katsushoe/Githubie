using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.GitHub;
using Githubie.Application.Interactive;
using Githubie.Application.Repositories;
using NSubstitute;
using Xunit;

namespace Githubie.Application.Tests;

public sealed class GitHubBranchProtectionGatewayTests
{
    private readonly IGitHubApiClient _api = Substitute.For<IGitHubApiClient>();
    private readonly IGitHubBranchProtectionBackend _backend = Substitute.For<IGitHubBranchProtectionBackend>();
    private readonly IInteractiveApprovalPrompt _prompt = Substitute.For<IInteractiveApprovalPrompt>();
    private readonly GitHubBranchProtectionGateway _gateway;
    private readonly GitHubBranchProtectionSnapshot _state = new("main", true,
        new JsonObject { ["allow_deletions"] = false }, new JsonObject { ["version"] = 1, ["secret_marker"] = "not-for-wire" });

    public GitHubBranchProtectionGatewayTests()
    {
        var repositories = new Dictionary<string, RepositoryOptions>
        {
            ["sample"] = new("owner", "repo", "C:\\fixture", "origin", "develop", "main",
                ["develop"], ["develop", "main"], ["main"], "main", "^v", "merge", true),
        };
        _gateway = new(new RepositoryAllowlist(repositories), _api, _backend, _prompt);
        _backend.ReadAsync("sample", "owner", "repo", "main", Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubBranchProtectionSnapshot>.Success(_state));
        _backend.Prepare(Arg.Any<GitHubBranchProtectionSnapshot?>(), Arg.Any<GitHubBranchProtectionEdit>())
            .Returns(_ => GitHubResult<GitHubBranchProtectionPlan>.Success(new(
                _state.Policy!.DeepClone().AsObject(), new JsonObject { ["allow_deletions"] = true }, _state.Raw.DeepClone().AsObject())));
        _prompt.RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalPromptOutcome.Approved());
        _backend.PutAsync("sample", "owner", "repo", "main", Arg.Any<JsonObject>(), Arg.Any<CancellationToken>())
            .Returns(GitHubResult<bool>.Success(true));
        _backend.DeleteAsync("sample", "owner", "repo", "main", Arg.Any<CancellationToken>())
            .Returns(GitHubResult<bool>.Success(true));
    }

    [Fact]
    public async Task Update_RequiresApprovalBeforeWriting()
    {
        _prompt.RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalPromptOutcome.Denied());
        var result = await _gateway.UpdateAsync("sample", "main", new(AllowDeletions: true), false, TestContext.Current.CancellationToken);
        result.Error.Should().Be(GitHubError.BranchProtectionApprovalDenied);
        await _backend.DidNotReceive().PutAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JsonObject>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ApprovalOutcome.TimedOut, GitHubError.BranchProtectionApprovalTimedOut)]
    [InlineData(ApprovalOutcome.NoInteractiveSession, GitHubError.BranchProtectionApprovalUnavailable)]
    [InlineData(ApprovalOutcome.LaunchFailed, GitHubError.BranchProtectionApprovalUnavailable)]
    public async Task Update_ApprovalUnavailable_FailsClosed(ApprovalOutcome outcome, GitHubError expected)
    {
        _prompt.RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalPromptOutcome.Failure(outcome));
        var result = await _gateway.UpdateAsync("sample", "main", new(AllowDeletions: true), false, TestContext.Current.CancellationToken);
        result.Error.Should().Be(expected);
        await _backend.DidNotReceive().PutAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JsonObject>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_ChangedAfterApproval_DoesNotWrite()
    {
        var changed = _state with { Raw = new JsonObject { ["version"] = 2 } };
        _backend.ReadAsync("sample", "owner", "repo", "main", Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubBranchProtectionSnapshot>.Success(_state), GitHubResult<GitHubBranchProtectionSnapshot>.Success(changed));
        var result = await _gateway.UpdateAsync("sample", "main", new(AllowDeletions: true), false, TestContext.Current.CancellationToken);
        result.Error.Should().Be(GitHubError.BranchProtectionChanged);
        await _backend.DidNotReceive().PutAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JsonObject>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_StableAndApproved_SendsExactlyOnce()
    {
        var result = await _gateway.UpdateAsync("sample", "main", new(AllowDeletions: true), false, TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue();
        result.Value!.Changed.Should().BeTrue();
        await _backend.Received(2).ReadAsync("sample", "owner", "repo", "main", Arg.Any<CancellationToken>());
        await _prompt.Received(1).RequestApprovalAsync(Arg.Is<ApprovalPromptRequest>(request =>
            request.Summary.Contains("owner/repo", StringComparison.Ordinal) && request.Summary.Contains("main", StringComparison.Ordinal)
            && request.Details.Any(detail => detail.StartsWith("Before: ", StringComparison.Ordinal))
                && request.Details.Any(detail => detail.StartsWith("After: ", StringComparison.Ordinal))
                && request.Details.Any(detail => detail.Contains("owner/repo", StringComparison.Ordinal))), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        await _backend.Received(1).PutAsync("sample", "owner", "repo", "main", Arg.Any<JsonObject>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_NoChangeStillRequiresApproval_ButDoesNotWrite()
    {
        _backend.Prepare(Arg.Any<GitHubBranchProtectionSnapshot?>(), Arg.Any<GitHubBranchProtectionEdit>())
            .Returns(_ => GitHubResult<GitHubBranchProtectionPlan>.Success(new(
                _state.Policy!.DeepClone().AsObject(), _state.Policy.DeepClone().AsObject(), _state.Raw.DeepClone().AsObject())));
        var result = await _gateway.UpdateAsync("sample", "main", new(AllowDeletions: false), false, TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue();
        result.Value!.Changed.Should().BeFalse();
        await _prompt.Received(1).RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        await _backend.DidNotReceive().PutAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JsonObject>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_UnknownMutationResult_IsNotReplayed()
    {
        _backend.PutAsync("sample", "owner", "repo", "main", Arg.Any<JsonObject>(), Arg.Any<CancellationToken>())
            .Returns(GitHubResult<bool>.Failure(GitHubError.BranchProtectionResultUnknown));
        var result = await _gateway.UpdateAsync("sample", "main", new(AllowDeletions: true), false, TestContext.Current.CancellationToken);
        result.Error.Should().Be(GitHubError.BranchProtectionResultUnknown);
        await _backend.Received(1).PutAsync("sample", "owner", "repo", "main", Arg.Any<JsonObject>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_MissingRuleRequiresExplicitCreateAndUnprotectedBranch()
    {
        _backend.ReadAsync("sample", "owner", "repo", "main", Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubBranchProtectionSnapshot>.Failure(GitHubError.BranchProtectionNotFoundOrInaccessible));
        var noCreate = await _gateway.UpdateAsync("sample", "main", new(AllowDeletions: false), false, TestContext.Current.CancellationToken);
        noCreate.Error.Should().Be(GitHubError.BranchProtectionNotFoundOrInaccessible);
        _api.GetBranchAsync("sample", "owner", "repo", "main", Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubBranchInfo>.Success(new("main", new string('a', 40), true)));
        var protectedBranch = await _gateway.UpdateAsync("sample", "main", new(AllowDeletions: false), true, TestContext.Current.CancellationToken);
        protectedBranch.Error.Should().Be(GitHubError.BranchProtectionUnsupportedPolicy);
        await _backend.DidNotReceive().PutAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JsonObject>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_ExplicitCreationChecksBranchTwiceAndRequiresApproval()
    {
        _backend.ReadAsync("sample", "owner", "repo", "main", Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubBranchProtectionSnapshot>.Failure(GitHubError.BranchProtectionNotFoundOrInaccessible));
        _api.GetBranchAsync("sample", "owner", "repo", "main", Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubBranchInfo>.Success(new("main", new string('a', 40), false)));
        _backend.Prepare(null, Arg.Any<GitHubBranchProtectionEdit>())
            .Returns(GitHubResult<GitHubBranchProtectionPlan>.Success(new(null, new JsonObject { ["enforce_admins"] = true }, null)));

        var result = await _gateway.UpdateAsync("sample", "main", new(EnforceAdmins: true), true, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        await _api.Received(2).GetBranchAsync("sample", "owner", "repo", "main", Arg.Any<CancellationToken>());
        await _prompt.Received(1).RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        await _backend.Received(1).PutAsync("sample", "owner", "repo", "main", Arg.Any<JsonObject>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_WhenApprovalDenied_DoesNotDelete()
    {
        _prompt.RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalPromptOutcome.Denied());
        var result = await _gateway.DeleteAsync("sample", "main", TestContext.Current.CancellationToken);
        result.Error.Should().Be(GitHubError.BranchProtectionApprovalDenied);
        await _backend.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_UnsupportedPolicy_DoesNotAskOrDelete()
    {
        _backend.ReadAsync("sample", "owner", "repo", "main", Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubBranchProtectionSnapshot>.Success(_state with { CanUpdate = false, Policy = null }));
        var result = await _gateway.DeleteAsync("sample", "main", TestContext.Current.CancellationToken);
        result.Error.Should().Be(GitHubError.BranchProtectionUnsupportedPolicy);
        await _prompt.DidNotReceive().RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        await _backend.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_StableAndApproved_SendsExactlyOnce()
    {
        var result = await _gateway.DeleteAsync("sample", "main", TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue();
        await _prompt.Received(1).RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        await _backend.Received(1).DeleteAsync("sample", "owner", "repo", "main", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Snapshot_DoesNotSerializeRawResponse()
    {
        var result = await _gateway.GetAsync("sample", "main", TestContext.Current.CancellationToken);
        JsonSerializer.Serialize(result.Value).Should().NotContain("not-for-wire").And.NotContain("secret_marker");
    }
}
