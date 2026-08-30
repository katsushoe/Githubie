using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.Credentials;
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
    private readonly IInteractiveTokenPrompt _tokenPrompt = Substitute.For<IInteractiveTokenPrompt>();
    private readonly IRepositoryConfigurationStore _store = Substitute.For<IRepositoryConfigurationStore>();
    private readonly RecordingTokenStore _tokenStore = new();
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
        _tokenPrompt.RequestTokenAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(InteractiveTokenPromptResult.Failure(InteractiveTokenPromptOutcome.Skipped));
    }

    [Fact]
    public async Task RegisterAsync_Approved_DerivesRemoteAndPersistsSafeDefaults()
    {
        var service = CreateService();

        var result = await service.RegisterAsync(
            new RepositoryRegistrationRequest("Sample", LocalRoot, null, null, null),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RepositoryId.Should().Be("sample");
        result.Value.GitHubOwner.Should().Be("derived-owner");
        result.Value.GitHubRepo.Should().Be("derived-repo");
        result.Value.TokenConfigured.Should().BeFalse();
        result.Value.TokenStatus.Should().Be("skipped");
        _allowlist.TryGet("sample", out var options).Should().BeTrue();
        options.DirectPushBranches.Should().Equal("develop");
        options.ProtectedBranches.Should().Equal("main");
        await _store.Received(1).SaveRepositoryAsync("sample", Arg.Is<RepositoryOptions>(x =>
            x.GitHubOwner == "derived-owner" && x.GitHubRepo == "derived-repo"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterAsync_TokenAccepted_SavesTokenWithoutReturningIt()
    {
        var token = "secret-token".ToCharArray();
        _tokenPrompt.RequestTokenAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(InteractiveTokenPromptResult.Accepted(token));
        var service = CreateService();

        var result = await service.RegisterAsync(
            new RepositoryRegistrationRequest("sample", LocalRoot, null, null, null),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TokenConfigured.Should().BeTrue();
        result.Value.TokenStatus.Should().Be("saved");
        _tokenStore.Repository.Should().Be("sample");
        _tokenStore.SavedToken.Should().Be("secret-token");
        result.ToString().Should().NotContain("secret-token");
        token.Should().OnlyContain(character => character == '\0');
    }

    [Fact]
    public async Task RegisterAsync_TokenSaveFails_KeepsRepositoryRegistered()
    {
        _tokenStore.SaveError = ApiTokenStoreError.AccessDenied;
        _tokenPrompt.RequestTokenAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(InteractiveTokenPromptResult.Accepted("secret-token".ToCharArray()));
        var service = CreateService();

        var result = await service.RegisterAsync(
            new RepositoryRegistrationRequest("sample", LocalRoot, null, null, null),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TokenConfigured.Should().BeFalse();
        result.Value.TokenStatus.Should().Be("save_failed");
        _allowlist.TryGet("sample", out _).Should().BeTrue();
    }

    [Fact]
    public async Task RegisterAsync_DuplicateId_RejectsBeforeApproval()
    {
        _allowlist.TryAdd("sample", CreateOptions());
        var service = CreateService();

        var result = await service.RegisterAsync(
            new RepositoryRegistrationRequest("SAMPLE", LocalRoot, null, null, null),
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
        _store,
        _tokenPrompt,
        _tokenStore);

    private static RepositoryOptions CreateOptions() => new(
        "owner", "repo", LocalRoot, "origin", "develop", "main",
        ["develop"], ["develop", "main"], ["main"], "main",
        "^v[0-9]+\\.[0-9]+\\.[0-9]+.*$", "merge", true);

    private sealed class RecordingTokenStore : IApiTokenStore
    {
        public string? Repository { get; private set; }
        public string? SavedToken { get; private set; }
        public ApiTokenStoreError? SaveError { get; set; }

        public ApiTokenStoreResult Save(string repositoryId, ReadOnlySpan<char> token)
        {
            Repository = repositoryId;
            SavedToken = token.ToString();
            return SaveError is null
                ? ApiTokenStoreResult.Success()
                : ApiTokenStoreResult.Failure(SaveError.Value);
        }

        public ApiTokenStoreReadResult Read(string repositoryId) =>
            ApiTokenStoreReadResult.Failure(ApiTokenStoreError.TokenNotFound);

        public ApiTokenStoreResult Delete(string repositoryId) => ApiTokenStoreResult.Success();

        public ApiTokenStoreResult Rename(string oldRepositoryId, string newRepositoryId) =>
            ApiTokenStoreResult.Success();
    }
}
