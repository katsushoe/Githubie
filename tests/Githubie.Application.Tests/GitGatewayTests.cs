using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.Git;
using Githubie.Application.Repositories;
using Githubie.Application.Interactive;
using NSubstitute;
using Xunit;

namespace Githubie.Application.Tests;

public sealed class GitGatewayTests
{
    private const string RepositoryId = "sample";
    private const string LocalRoot = "C:\\repo";
    private const string OldSha = "1111111111111111111111111111111111111111";
    private const string NewSha = "2222222222222222222222222222222222222222";

    private readonly IGitCommandClient _commandClient = Substitute.For<IGitCommandClient>();
    private readonly IRepositoryEnvironment _environment = Substitute.For<IRepositoryEnvironment>();
    private readonly IInteractiveApprovalPrompt _approvalPrompt = Substitute.For<IInteractiveApprovalPrompt>();
    private readonly GitGateway _gateway;

    public GitGatewayTests()
    {
        _environment.GetFullPath(Arg.Any<string>()).Returns(callInfo => callInfo.Arg<string>());
        _environment.DirectoryExists(Arg.Any<string>()).Returns(true);
        _environment.GitMetadataExists(Arg.Any<string>()).Returns(true);
        _environment.ContainsReparsePoint(Arg.Any<string>()).Returns(false);

        var allowlist = new RepositoryAllowlist(new Dictionary<string, RepositoryOptions>
        {
            [RepositoryId] = CreateOptions(),
        });

        _gateway = new GitGateway(allowlist, new LocalPathValidator(_environment), _commandClient, _approvalPrompt);
    }

    private static RepositoryOptions CreateOptions(bool requireCleanWorkingTree = true) => new(
        GitHubOwner: "owner",
        GitHubRepo: "repo",
        LocalRoot: LocalRoot,
        Remote: "origin",
        DevelopBranch: "develop",
        MainBranch: "main",
        DirectPushBranches: ["develop"],
        PullBranches: ["develop", "main"],
        ProtectedBranches: ["main"],
        TagTargetBranch: "main",
        TagPattern: "^v[0-9]+\\.[0-9]+\\.[0-9]+.*$",
        MergeMethod: "merge",
        RequireCleanWorkingTree: requireCleanWorkingTree);

    [Fact]
    public async Task GetStatusAsync_ResolvesRepositoryIdCaseInsensitively()
    {
        _commandClient.GetCurrentBranchAsync(LocalRoot, Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success("develop"));
        _commandClient.GetHeadAsync(LocalRoot, Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success(NewSha));
        _commandClient.GetRemoteHeadAsync(LocalRoot, "origin", "develop", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success(OldSha));
        _commandClient.GetRemoteHeadAsync(LocalRoot, "origin", "main", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success(OldSha));
        _commandClient.GetAheadBehindAsync(LocalRoot, "origin", "develop", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success("1 0"));
        _commandClient.GetStatusAsync(LocalRoot, Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success(string.Empty));

        var result = await _gateway.GetStatusAsync("SAMPLE", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    private void SetUpPushPreconditions(string currentBranch, bool workingTreeClean, string remoteUrl = "https://github.com/owner/repo.git", int ahead = 1, int behind = 0)
    {
        _commandClient.GetCurrentBranchAsync(LocalRoot, Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success(currentBranch));
        _commandClient.GetRemoteUrlAsync(LocalRoot, "origin", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success(remoteUrl));
        _commandClient.GetStatusAsync(LocalRoot, Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success(workingTreeClean ? string.Empty : " M file.txt"));
        _commandClient.GetRemoteRefAsync(LocalRoot, RepositoryId, "origin", $"refs/heads/{currentBranch}", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success($"{OldSha}\trefs/heads/{currentBranch}"));
        _commandClient.GetAheadBehindAsync(LocalRoot, "origin", currentBranch, Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success($"{ahead} {behind}"));
    }

    private void SetUpRewritePreconditions(string remoteSha = OldSha)
    {
        _commandClient.GetRemoteUrlAsync(LocalRoot, "origin", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success("https://github.com/owner/repo.git"));
        _commandClient.GetLocalRefAsync(LocalRoot, NewSha, Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success(NewSha));
        _commandClient.GetRemoteRefAsync(LocalRoot, RepositoryId, "origin", "refs/heads/main", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success($"{remoteSha}\trefs/heads/main"));
    }

    private static GitHistoryRewriteRef RewriteRef(string expected = OldSha) =>
        new("refs/heads/main", NewSha, expected);

    [Fact]
    public async Task PushTagAsync_PushesExistingAllowedLocalTag()
    {
        const string tag = "v1.2.3";
        _commandClient.GetRemoteUrlAsync(LocalRoot, "origin", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success("https://github.com/owner/repo.git"));
        _commandClient.GetLocalRefAsync(LocalRoot, $"refs/tags/{tag}", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success(NewSha));
        _commandClient.GetRemoteRefAsync(LocalRoot, RepositoryId, "origin", $"refs/tags/{tag}", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success(string.Empty));
        _commandClient.PushTagAsync(LocalRoot, RepositoryId, "origin", tag, Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success(string.Empty));

        var result = await _gateway.PushTagAsync(RepositoryId, tag, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task PushTagAsync_WhenLocalTagIsMissing_ReturnsInvalidRef()
    {
        _commandClient.GetRemoteUrlAsync(LocalRoot, "origin", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success("https://github.com/owner/repo.git"));
        _commandClient.GetLocalRefAsync(LocalRoot, "refs/tags/v1.2.3", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Failed(GitCommandFailure.Failed, standardError: "unknown revision"));

        var result = await _gateway.PushTagAsync(RepositoryId, "v1.2.3", TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitGatewayError.InvalidRef);
        result.Diagnostic.Should().Be("The local tag ref does not exist.");
    }

    [Fact]
    public async Task PushTagAsync_WhenLightweightRemoteTagMatches_ReturnsNothingToPush()
    {
        const string tag = "v1.2.3";
        _commandClient.GetRemoteUrlAsync(LocalRoot, "origin", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success("https://github.com/owner/repo.git"));
        _commandClient.GetLocalRefAsync(LocalRoot, $"refs/tags/{tag}", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success(NewSha));
        _commandClient.GetRemoteRefAsync(LocalRoot, RepositoryId, "origin", $"refs/tags/{tag}", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success($"{NewSha}\trefs/tags/{tag}"));

        var result = await _gateway.PushTagAsync(RepositoryId, tag, TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitGatewayError.NothingToPush);
        result.Diagnostic.Should().Be("The remote tag already points to the same object.");
        await _commandClient.DidNotReceive().PushTagAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PushTagAsync_WhenAnnotatedRemoteTagMatches_ReturnsNothingToPush()
    {
        const string tag = "v1.2.3";
        _commandClient.GetRemoteUrlAsync(LocalRoot, "origin", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success("https://github.com/owner/repo.git"));
        _commandClient.GetLocalRefAsync(LocalRoot, $"refs/tags/{tag}", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success(OldSha));
        _commandClient.GetRemoteRefAsync(LocalRoot, RepositoryId, "origin", $"refs/tags/{tag}", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success($"{OldSha}\trefs/tags/{tag}"));

        var result = await _gateway.PushTagAsync(RepositoryId, tag, TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitGatewayError.NothingToPush);
        result.Diagnostic.Should().Be("The remote tag already points to the same object.");
    }

    [Fact]
    public async Task PushTagAsync_WhenRemoteTagDiffers_ReturnsNonFastForward()
    {
        const string tag = "v1.2.3";
        _commandClient.GetRemoteUrlAsync(LocalRoot, "origin", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success("https://github.com/owner/repo.git"));
        _commandClient.GetLocalRefAsync(LocalRoot, $"refs/tags/{tag}", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success(NewSha));
        _commandClient.GetRemoteRefAsync(LocalRoot, RepositoryId, "origin", $"refs/tags/{tag}", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success($"{OldSha}\trefs/tags/{tag}"));

        var result = await _gateway.PushTagAsync(RepositoryId, tag, TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitGatewayError.NonFastForward);
        result.Diagnostic.Should().Be("The remote tag points to a different object; overwrite is not allowed.");
        await _commandClient.DidNotReceive().PushTagAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PushTagAsync_WhenTagViolatesPolicy_ReturnsDiagnosticInvalidRef()
    {
        var result = await _gateway.PushTagAsync(
            RepositoryId, "invalid tag", TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitGatewayError.InvalidRef);
        result.Diagnostic.Should().Be("The tag name does not match the configured tag policy.");
        await _commandClient.DidNotReceive().GetLocalRefAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RewriteHistoryAsync_DryRun_ReturnsPlanWithoutApprovalOrPush()
    {
        SetUpRewritePreconditions();

        var result = await _gateway.RewriteHistoryAsync(RepositoryId, [RewriteRef()], true, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.DryRun.Should().BeTrue();
        result.Value.Refs.Single().Should().BeEquivalentTo(
            new GitHistoryRewriteRefResult("refs/heads/main", OldSha, NewSha, false, null));
        await _approvalPrompt.DidNotReceive().RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        await _commandClient.DidNotReceive().PushHistoryRewriteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<GitHistoryRewriteRef>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RewriteHistoryAsync_LeaseMismatch_RejectsBeforeApproval()
    {
        SetUpRewritePreconditions();

        var result = await _gateway.RewriteHistoryAsync(
            RepositoryId, [RewriteRef("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")], false, CancellationToken.None);

        result.Error.Should().Be(GitGatewayError.LeaseConflict);
        await _approvalPrompt.DidNotReceive().RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RewriteHistoryAsync_ApprovalDenied_DoesNotPush()
    {
        SetUpRewritePreconditions();
        _approvalPrompt.RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalPromptOutcome.Denied());

        var result = await _gateway.RewriteHistoryAsync(RepositoryId, [RewriteRef()], false, CancellationToken.None);

        result.Error.Should().Be(GitGatewayError.ApprovalDenied);
        await _commandClient.DidNotReceive().PushHistoryRewriteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<GitHistoryRewriteRef>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RewriteHistoryAsync_Approved_RechecksLeaseAndPushesAtomically()
    {
        SetUpRewritePreconditions();
        _approvalPrompt.RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalPromptOutcome.Approved());
        _commandClient.PushHistoryRewriteAsync(LocalRoot, RepositoryId, "origin", Arg.Any<IReadOnlyList<GitHistoryRewriteRef>>(), Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success(string.Empty));

        var result = await _gateway.RewriteHistoryAsync(RepositoryId, [RewriteRef()], false, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Approval.Should().Be("approved");
        await _commandClient.Received(2).GetRemoteRefAsync(LocalRoot, RepositoryId, "origin", "refs/heads/main", Arg.Any<CancellationToken>());
        await _commandClient.Received(1).PushHistoryRewriteAsync(LocalRoot, RepositoryId, "origin", Arg.Any<IReadOnlyList<GitHistoryRewriteRef>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RewriteHistoryAsync_AtomicUnsupported_ReturnsSpecificError()
    {
        SetUpRewritePreconditions();
        _approvalPrompt.RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalPromptOutcome.Approved());
        _commandClient.PushHistoryRewriteAsync(LocalRoot, RepositoryId, "origin", Arg.Any<IReadOnlyList<GitHistoryRewriteRef>>(), Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Failed(GitCommandFailure.Failed, standardError: "the receiving end does not support --atomic push"));

        var result = await _gateway.RewriteHistoryAsync(RepositoryId, [RewriteRef()], false, CancellationToken.None);

        result.Error.Should().Be(GitGatewayError.AtomicNotSupported);
    }

    [Fact]
    public async Task RewriteHistoryAsync_PermissionDenied_ReturnsSpecificError()
    {
        SetUpRewritePreconditions();
        _approvalPrompt.RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalPromptOutcome.Approved());
        _commandClient.PushHistoryRewriteAsync(LocalRoot, RepositoryId, "origin", Arg.Any<IReadOnlyList<GitHistoryRewriteRef>>(), Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Failed(GitCommandFailure.Failed, standardError: "remote: Permission denied"));

        var result = await _gateway.RewriteHistoryAsync(RepositoryId, [RewriteRef()], false, CancellationToken.None);

        result.Error.Should().Be(GitGatewayError.PermissionDenied);
    }

    [Theory]
    [InlineData("remote: error: GH013: Repository rule violations found", GitGatewayError.BranchProtectionDenied)]
    [InlineData("refusing to allow a Personal Access Token to create or update workflow", GitGatewayError.WorkflowPermissionDenied)]
    [InlineData("remote: Write access to repository not granted. fatal: requested URL returned error: 403", GitGatewayError.TokenPermissionDenied)]
    [InlineData("! [rejected] main -> main (stale info)", GitGatewayError.LeaseConflict)]
    public async Task RewriteHistoryAsync_KnownRemoteRejection_ReturnsSafeSpecificError(
        string standardError, GitGatewayError expected)
    {
        SetUpRewritePreconditions();
        _approvalPrompt.RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalPromptOutcome.Approved());
        _commandClient.PushHistoryRewriteAsync(LocalRoot, RepositoryId, "origin", Arg.Any<IReadOnlyList<GitHistoryRewriteRef>>(), Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Failed(GitCommandFailure.Failed, standardError: standardError));

        var result = await _gateway.RewriteHistoryAsync(RepositoryId, [RewriteRef()], false, CancellationToken.None);

        result.Error.Should().Be(expected);
    }

    [Fact]
    public async Task PushAsync_AllowsCleanDevelopPush()
    {
        SetUpPushPreconditions("develop", workingTreeClean: true);
        _commandClient.PushAsync(LocalRoot, RepositoryId, "origin", "develop", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success(string.Empty));

        var result = await _gateway.PushAsync(RepositoryId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _commandClient.Received(1).PushAsync(LocalRoot, RepositoryId, "origin", "develop", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PushAsync_WhenRemoteBranchDoesNotExist_CreatesBranchWithoutAheadCheck()
    {
        SetUpPushPreconditions("develop", workingTreeClean: true);
        _commandClient.GetRemoteRefAsync(LocalRoot, RepositoryId, "origin", "refs/heads/develop", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success(string.Empty));
        _commandClient.PushAsync(LocalRoot, RepositoryId, "origin", "develop", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success(string.Empty));

        var result = await _gateway.PushAsync(RepositoryId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _commandClient.DidNotReceive().GetAheadBehindAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _commandClient.Received(1).PushAsync(
            LocalRoot, RepositoryId, "origin", "develop", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("fatal: Authentication failed", GitGatewayError.AuthenticationFailed)]
    [InlineData("remote: Write access to repository not granted", GitGatewayError.PermissionDenied)]
    public async Task PushAsync_WhenRemoteBranchLookupFails_ReturnsSpecificError(
        string standardError, GitGatewayError expected)
    {
        SetUpPushPreconditions("develop", workingTreeClean: true);
        _commandClient.GetRemoteRefAsync(LocalRoot, RepositoryId, "origin", "refs/heads/develop", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Failed(GitCommandFailure.Failed, standardError: standardError));

        var result = await _gateway.PushAsync(RepositoryId, CancellationToken.None);

        result.Error.Should().Be(expected);
        await _commandClient.DidNotReceive().PushAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PushAsync_DeniesProtectedBranch_AndNeverCallsPush()
    {
        SetUpPushPreconditions("main", workingTreeClean: true);

        var result = await _gateway.PushAsync(RepositoryId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GitGatewayError.ProtectedBranch);
        await _commandClient.DidNotReceive().PushAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PushAsync_DeniesUnlistedBranch()
    {
        SetUpPushPreconditions("feature/x", workingTreeClean: true);

        var result = await _gateway.PushAsync(RepositoryId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GitGatewayError.BranchNotAllowed);
    }

    [Fact]
    public async Task PushAsync_DeniesDirtyWorkingTree()
    {
        SetUpPushPreconditions("develop", workingTreeClean: false);

        var result = await _gateway.PushAsync(RepositoryId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GitGatewayError.WorkingTreeDirty);
    }

    [Fact]
    public async Task PushAsync_DeniesRemoteMismatch_BeforePolicyEvaluation()
    {
        SetUpPushPreconditions("develop", workingTreeClean: true, remoteUrl: "https://github.com/attacker/other-repo.git");

        var result = await _gateway.PushAsync(RepositoryId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GitGatewayError.RemoteMismatch);
        await _commandClient.DidNotReceive().PushAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PushAsync_DeniesSshRemoteWithSpecificError()
    {
        SetUpPushPreconditions("develop", workingTreeClean: true, remoteUrl: "git@github.com:owner/repo.git");

        var result = await _gateway.PushAsync(RepositoryId, CancellationToken.None);

        result.Error.Should().Be(GitGatewayError.RemoteHttpsRequired);
        await _commandClient.DidNotReceive().PushAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PushAsync_WhenAheadIsZero_ReturnsNothingToPushWithoutCallingPush()
    {
        SetUpPushPreconditions("develop", workingTreeClean: true, ahead: 0);

        var result = await _gateway.PushAsync(RepositoryId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GitGatewayError.NothingToPush);
        await _commandClient.DidNotReceive().PushAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("fatal: Authentication failed for 'https://github.com/owner/repo.git/'", GitGatewayError.AuthenticationFailed)]
    [InlineData("remote: Write access to repository not granted. fatal: requested URL returned error: 403", GitGatewayError.PermissionDenied)]
    [InlineData("! [rejected] develop -> develop (fetch first)", GitGatewayError.NonFastForward)]
    [InlineData("fatal: unable to access remote: connection reset", GitGatewayError.NetworkError)]
    public async Task PushAsync_WhenGitFails_ClassifiesStandardError(string standardError, GitGatewayError expected)
    {
        SetUpPushPreconditions("develop", workingTreeClean: true, ahead: 1);
        _commandClient.PushAsync(LocalRoot, RepositoryId, "origin", "develop", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Failed(GitCommandFailure.Failed, standardError: standardError, exitCode: 128));

        var result = await _gateway.PushAsync(RepositoryId, CancellationToken.None);

        result.Error.Should().Be(expected);
        result.Diagnostic.Should().Be(standardError);
        result.ExitCode.Should().Be(128);
    }

    [Fact]
    public async Task PullAsync_DeniesBranchNotInPullBranches_AndNeverCallsPull()
    {
        var result = await _gateway.PullAsync(RepositoryId, "feature/x", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GitGatewayError.BranchNotAllowed);
        await _commandClient.DidNotReceive().PullFastForwardOnlyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PullAsync_MapsFailedPullToNonFastForward()
    {
        _commandClient.PullFastForwardOnlyAsync(LocalRoot, RepositoryId, "origin", "main", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Failed(GitCommandFailure.Failed, standardError: "fatal: Not possible to fast-forward, aborting."));

        var result = await _gateway.PullAsync(RepositoryId, "main", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GitGatewayError.NonFastForward);
    }

    [Theory]
    [InlineData("fatal: Authentication failed", GitGatewayError.AuthenticationFailed)]
    [InlineData("fatal: unable to access remote: Could not resolve host: github.com", GitGatewayError.NetworkError)]
    [InlineData("remote: Repository not found.", GitGatewayError.RemoteUnavailable)]
    public async Task FetchAsync_WhenGitFails_ReturnsSpecificSafeCategory(string standardError, GitGatewayError expected)
    {
        _commandClient.FetchAsync(LocalRoot, RepositoryId, "origin", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Failed(GitCommandFailure.Failed, standardError: standardError));

        var result = await _gateway.FetchAsync(RepositoryId, CancellationToken.None);

        result.Error.Should().Be(expected);
    }

    [Fact]
    public async Task PullAsync_Succeeds()
    {
        _commandClient.PullFastForwardOnlyAsync(LocalRoot, RepositoryId, "origin", "main", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success(string.Empty));

        var result = await _gateway.PullAsync(RepositoryId, "main", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetStatusAsync_DeniesUnregisteredRepository()
    {
        var result = await _gateway.GetStatusAsync("notregistered", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GitGatewayError.RepositoryNotAllowed);
    }

    [Fact]
    public async Task GetStatusAsync_DeniesInvalidRepositoryIdFormat()
    {
        var result = await _gateway.GetStatusAsync("has spaces/invalid", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GitGatewayError.RepositoryNotFound);
    }

    [Fact]
    public async Task GetStatusAsync_DeniesMissingLocalRoot()
    {
        _environment.DirectoryExists(Arg.Any<string>()).Returns(false);

        var result = await _gateway.GetStatusAsync(RepositoryId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GitGatewayError.LocalRootNotFound);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsSnapshotOnSuccess()
    {
        _commandClient.GetCurrentBranchAsync(LocalRoot, Arg.Any<CancellationToken>()).Returns(GitCommandResult.Success("develop"));
        _commandClient.GetHeadAsync(LocalRoot, Arg.Any<CancellationToken>()).Returns(GitCommandResult.Success("abc123"));
        _commandClient.GetRemoteHeadAsync(LocalRoot, "origin", "develop", Arg.Any<CancellationToken>()).Returns(GitCommandResult.Success("abc123"));
        _commandClient.GetRemoteHeadAsync(LocalRoot, "origin", "main", Arg.Any<CancellationToken>()).Returns(GitCommandResult.Success("def456"));
        _commandClient.GetAheadBehindAsync(LocalRoot, "origin", "develop", Arg.Any<CancellationToken>()).Returns(GitCommandResult.Success("2\t0"));
        _commandClient.GetStatusAsync(LocalRoot, Arg.Any<CancellationToken>()).Returns(GitCommandResult.Success(string.Empty));

        var result = await _gateway.GetStatusAsync(RepositoryId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.LocalBranch.Should().Be("develop");
        result.Value.LocalHead.Should().Be("abc123");
        result.Value.Ahead.Should().Be(2);
        result.Value.Behind.Should().Be(0);
        result.Value.WorkingTreeClean.Should().BeTrue();
    }

    [Fact]
    public async Task GetStatusAsync_DirtyTree_ReturnsOnlyStatusAndRelativePaths()
    {
        _commandClient.GetCurrentBranchAsync(LocalRoot, Arg.Any<CancellationToken>()).Returns(GitCommandResult.Success("develop"));
        _commandClient.GetHeadAsync(LocalRoot, Arg.Any<CancellationToken>()).Returns(GitCommandResult.Success(NewSha));
        _commandClient.GetRemoteHeadAsync(LocalRoot, "origin", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(GitCommandResult.Success(OldSha));
        _commandClient.GetAheadBehindAsync(LocalRoot, "origin", "develop", Arg.Any<CancellationToken>()).Returns(GitCommandResult.Success("1 0"));
        _commandClient.GetStatusAsync(LocalRoot, Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success("?? .claude/settings.local.json\n M src/file.cs"));

        var result = await _gateway.GetStatusAsync(RepositoryId, CancellationToken.None);

        result.Value!.WorkingTreeClean.Should().BeFalse();
        result.Value.WorkingTreeChanges.Should().Equal(
            new GitWorkingTreeChange("??", ".claude/settings.local.json"),
            new GitWorkingTreeChange(" M", "src/file.cs"));
    }
}
