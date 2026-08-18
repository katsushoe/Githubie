using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.Git;
using Githubie.Application.Repositories;
using NSubstitute;
using Xunit;

namespace Githubie.Application.Tests;

public sealed class GitGatewayTests
{
    private const string RepositoryId = "sample";
    private const string LocalRoot = "C:\\repo";

    private readonly IGitCommandClient _commandClient = Substitute.For<IGitCommandClient>();
    private readonly IRepositoryEnvironment _environment = Substitute.For<IRepositoryEnvironment>();
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

        _gateway = new GitGateway(allowlist, new LocalPathValidator(_environment), _commandClient);
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

    private void SetUpPushPreconditions(string currentBranch, bool workingTreeClean, string remoteUrl = "https://github.com/owner/repo.git")
    {
        _commandClient.GetCurrentBranchAsync(LocalRoot, Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success(currentBranch));
        _commandClient.GetRemoteUrlAsync(LocalRoot, "origin", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success(remoteUrl));
        _commandClient.GetStatusAsync(LocalRoot, Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success(workingTreeClean ? string.Empty : " M file.txt"));
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
    public async Task PushAsync_MapsFailedPushToNothingToPush()
    {
        SetUpPushPreconditions("develop", workingTreeClean: true);
        _commandClient.PushAsync(LocalRoot, RepositoryId, "origin", "develop", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Failed(GitCommandFailure.Failed));

        var result = await _gateway.PushAsync(RepositoryId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GitGatewayError.NothingToPush);
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
            .Returns(GitCommandResult.Failed(GitCommandFailure.Failed));

        var result = await _gateway.PullAsync(RepositoryId, "main", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GitGatewayError.NonFastForward);
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
        var result = await _gateway.GetStatusAsync("not-registered", CancellationToken.None);

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
}
