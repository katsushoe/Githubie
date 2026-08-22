using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.Git;
using Githubie.Application.Interactive;
using Githubie.Application.Repositories;
using NSubstitute;
using Xunit;

namespace Githubie.Application.Tests;

public sealed class RepositoryRegistrationServiceTests
{
    private const string LocalRoot = "C:\\repos\\sample";
    private readonly IRepositoryEnvironment _environment = Substitute.For<IRepositoryEnvironment>();
    private readonly IGitCommandClient _git = Substitute.For<IGitCommandClient>();
    private readonly IInteractiveApprovalPrompt _approval = Substitute.For<IInteractiveApprovalPrompt>();
    private readonly IRepositoryConfigurationStore _store = Substitute.For<IRepositoryConfigurationStore>();
    private readonly RepositoryAllowlist _allowlist = new(new Dictionary<string, RepositoryOptions>());

    public RepositoryRegistrationServiceTests()
    {
        _environment.GetFullPath(LocalRoot).Returns(LocalRoot);
        _environment.DirectoryExists(LocalRoot).Returns(true);
        _environment.GitMetadataExists(LocalRoot).Returns(true);
        _environment.ContainsReparsePoint(LocalRoot).Returns(false);
        _git.GetRemoteUrlAsync(LocalRoot, "origin", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success("https://github.com/derived-owner/derived-repo.git"));
        _approval.RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalPromptOutcome.Approved());
    }

    [Fact]
    public async Task RegisterAsync_Approved_DerivesRemoteAndPersistsSafeDefaults()
    {
        var service = CreateService();

        var result = await service.RegisterAsync(
            new RepositoryRegistrationRequest("sample", LocalRoot, null, null, null),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.GitHubOwner.Should().Be("derived-owner");
        result.Value.GitHubRepo.Should().Be("derived-repo");
        _allowlist.TryGet("sample", out var options).Should().BeTrue();
        options.DirectPushBranches.Should().Equal("develop");
        options.ProtectedBranches.Should().Equal("main");
        await _store.Received(1).SaveRepositoryAsync("sample", Arg.Is<RepositoryOptions>(x =>
            x.GitHubOwner == "derived-owner" && x.GitHubRepo == "derived-repo"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterAsync_DuplicateId_RejectsBeforeApproval()
    {
        _allowlist.TryAdd("sample", CreateOptions());
        var service = CreateService();

        var result = await service.RegisterAsync(
            new RepositoryRegistrationRequest("sample", LocalRoot, null, null, null),
            TestContext.Current.CancellationToken);

        result.Error.Should().Be(RepositoryRegistrationError.DuplicateRepositoryId);
        await _approval.DidNotReceive().RequestApprovalAsync(
            Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterAsync_MissingLocalRoot_ReturnsSpecificError()
    {
        _environment.DirectoryExists(LocalRoot).Returns(false);
        var service = CreateService();

        var result = await service.RegisterAsync(
            new RepositoryRegistrationRequest("sample", LocalRoot, null, null, null),
            TestContext.Current.CancellationToken);

        result.Error.Should().Be(RepositoryRegistrationError.InvalidLocalRoot);
    }

    [Fact]
    public async Task RegisterAsync_InvalidRemote_ReturnsSpecificError()
    {
        _git.GetRemoteUrlAsync(LocalRoot, "missing", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Failed(GitCommandFailure.Failed));
        var service = CreateService();

        var result = await service.RegisterAsync(
            new RepositoryRegistrationRequest("sample", LocalRoot, "missing", null, null),
            TestContext.Current.CancellationToken);

        result.Error.Should().Be(RepositoryRegistrationError.InvalidRemote);
    }

    [Fact]
    public async Task RegisterAsync_RemoteBeginningWithHyphen_IsRejectedBeforeGitExecution()
    {
        var service = CreateService();

        var result = await service.RegisterAsync(
            new RepositoryRegistrationRequest("sample", LocalRoot, "--upload-pack=evil", null, null),
            TestContext.Current.CancellationToken);

        result.Error.Should().Be(RepositoryRegistrationError.InvalidRemote);
        await _git.DidNotReceive().GetRemoteUrlAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterAsync_NonGitHubRemote_IsRejected()
    {
        _git.GetRemoteUrlAsync(LocalRoot, "origin", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success("git@example.com:owner/repo.git"));
        var service = CreateService();

        var result = await service.RegisterAsync(
            new RepositoryRegistrationRequest("sample", LocalRoot, null, null, null),
            TestContext.Current.CancellationToken);

        result.Error.Should().Be(RepositoryRegistrationError.NonGitHubRemote);
    }

    [Fact]
    public async Task RegisterAsync_SshGitHubRemote_RequiresHttps()
    {
        _git.GetRemoteUrlAsync(LocalRoot, "origin", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success("git@github.com:owner/repo.git"));
        var service = CreateService();

        var result = await service.RegisterAsync(
            new RepositoryRegistrationRequest("sample", LocalRoot, null, null, null),
            TestContext.Current.CancellationToken);

        result.Error.Should().Be(RepositoryRegistrationError.RemoteHttpsRequired);
        await _approval.DidNotReceive().RequestApprovalAsync(
            Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterAsync_ApprovalDenied_DoesNotPersist()
    {
        _approval.RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalPromptOutcome.Denied());
        var service = CreateService();

        var result = await service.RegisterAsync(
            new RepositoryRegistrationRequest("sample", LocalRoot, null, null, null),
            TestContext.Current.CancellationToken);

        result.Error.Should().Be(RepositoryRegistrationError.ApprovalDenied);
        await _store.DidNotReceive().SaveRepositoryAsync(
            Arg.Any<string>(), Arg.Any<RepositoryOptions>(), Arg.Any<CancellationToken>());
    }

    private RepositoryRegistrationService CreateService() => new(
        _allowlist,
        new LocalPathValidator(_environment),
        _git,
        _approval,
        _store);

    private static RepositoryOptions CreateOptions() => new(
        "owner", "repo", LocalRoot, "origin", "develop", "main",
        ["develop"], ["develop", "main"], ["main"], "main",
        "^v[0-9]+\\.[0-9]+\\.[0-9]+.*$", "merge", true);
}
