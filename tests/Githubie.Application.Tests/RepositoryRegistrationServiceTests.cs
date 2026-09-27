using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.Credentials;
using Githubie.Application.Git;
using Githubie.Application.GitHub;
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
    private readonly IGitHubApiClient _gitHub = Substitute.For<IGitHubApiClient>();
    private readonly RepositoryAllowlist _allowlist = new(new Dictionary<string, RepositoryOptions>());

    public RepositoryRegistrationServiceTests()
    {
        _environment.GetFullPath(LocalRoot).Returns(LocalRoot);
        _environment.DirectoryExists(LocalRoot).Returns(true);
        _environment.GitMetadataExists(LocalRoot).Returns(true);
        _environment.ContainsReparsePoint(LocalRoot).Returns(false);
        _git.GetRemoteUrlAsync(LocalRoot, "origin", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success("https://github.com/derived-owner/derived-repo.git"));
        _git.GetLocalConfigAsync(LocalRoot, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Failed(GitCommandFailure.Failed));
        _approval.RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalPromptOutcome.Approved());
        _tokenPrompt.RequestTokenAsync(Arg.Any<TokenPromptRequest>(), Arg.Any<CancellationToken>())
            .Returns(InteractiveTokenPromptResult.Failure(InteractiveTokenPromptOutcome.Skipped));
    }

    [Fact]
    public async Task RegisterAsync_ExplicitAuthor_StoresIdentity()
    {
        var service = CreateService();

        var result = await service.RegisterAsync(
            new RepositoryRegistrationRequest("sample", LocalRoot, null, null, null, "Writer", "writer@example.com"),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        _allowlist.TryGet("sample", out var options).Should().BeTrue();
        options.CommitAuthorName.Should().Be("Writer");
        options.CommitAuthorEmail.Should().Be("writer@example.com");
    }

    [Fact]
    public async Task RegisterAsync_LocalAuthor_StoresIdentityWhenNotExplicit()
    {
        _git.GetLocalConfigAsync(LocalRoot, "user.name", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success("Local Writer"));
        _git.GetLocalConfigAsync(LocalRoot, "user.email", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success("local@example.com"));
        var service = CreateService();

        var result = await service.RegisterAsync(
            new RepositoryRegistrationRequest("sample", LocalRoot, null, null, null),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        _allowlist.TryGet("sample", out var options).Should().BeTrue();
        options.CommitAuthorName.Should().Be("Local Writer");
        options.CommitAuthorEmail.Should().Be("local@example.com");
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
        await _tokenPrompt.Received(1).RequestTokenAsync(
            Arg.Is<TokenPromptRequest>(x => x.ProjectName == "sample" &&
                x.RepositoryUrl == "https://github.com/derived-owner/derived-repo"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterAsync_TokenAccepted_SavesTokenWithoutReturningIt()
    {
        var token = "secret-token".ToCharArray();
        _tokenPrompt.RequestTokenAsync(Arg.Any<TokenPromptRequest>(), Arg.Any<CancellationToken>())
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
        _tokenPrompt.RequestTokenAsync(Arg.Any<TokenPromptRequest>(), Arg.Any<CancellationToken>())
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

    [Fact]
    public async Task CreateAsync_WithLocalRoot_CreatesRepositorySetsOriginAndRegisters()
    {
        ArrangeLocalRootWithoutOrigin();
        ArrangeTokenEntered();
        ArrangeLogin("owner");
        ArrangeCreated(isPrivate: true);
        var service = CreateService();

        var result = await service.CreateAsync(
            new RepositoryCreateRequest("newrepo", "owner", "NewRepo", null, "desc", LocalRoot),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Registered.Should().BeTrue();
        result.Value.Private.Should().BeTrue();
        result.Value.TokenStatus.Should().Be("saved");
        await _gitHub.Received(1).CreateRepositoryAsync(
            "newrepo",
            Arg.Is<GitHubRepositoryCreate>(x => x.Owner == "owner" && x.Name == "NewRepo" && x.Private
                && x.Description == "desc" && !x.ForOrganization),
            Arg.Any<CancellationToken>());
        await _git.Received(1).AddRemoteAsync(
            LocalRoot, "origin", "https://github.com/owner/NewRepo.git", Arg.Any<CancellationToken>());
        _allowlist.TryGet("newrepo", out var options).Should().BeTrue();
        options.GitHubOwner.Should().Be("owner");
        options.GitHubRepo.Should().Be("NewRepo");
        _tokenStore.Repository.Should().Be("newrepo");
    }

    [Fact]
    public async Task CreateAsync_OwnerDiffersFromTokenUser_CreatesUnderOrganization()
    {
        ArrangeTokenEntered();
        ArrangeLogin("someone");
        ArrangeCreated(isPrivate: false, owner: "acme");
        var service = CreateService();

        var result = await service.CreateAsync(
            new RepositoryCreateRequest("acmerepo", "acme", "NewRepo", "public", null, null),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Registered.Should().BeFalse();
        await _gitHub.Received(1).CreateRepositoryAsync(
            "acmerepo",
            Arg.Is<GitHubRepositoryCreate>(x => x.ForOrganization && !x.Private),
            Arg.Any<CancellationToken>());
        await _git.DidNotReceive().AddRemoteAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _allowlist.TryGet("acmerepo", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(GitHubError.RepositoryAlreadyExists, RepositoryRegistrationError.GitHubRepositoryAlreadyExists)]
    [InlineData(GitHubError.PermissionDenied, RepositoryRegistrationError.GitHubPermissionDenied)]
    [InlineData(GitHubError.AuthenticationFailed, RepositoryRegistrationError.GitHubAuthenticationFailed)]
    [InlineData(GitHubError.ApiError, RepositoryRegistrationError.GitHubFailed)]
    public async Task CreateAsync_GitHubRejects_ReturnsDistinctErrorKeepsReasonAndRemovesNewToken(
        GitHubError gitHubError, RepositoryRegistrationError expected)
    {
        const string reason = "GitHub HTTP 422: Repository creation failed. [name already exists on this account]";
        ArrangeLocalRootWithoutOrigin();
        ArrangeTokenEntered();
        ArrangeLogin("owner");
        _gitHub.CreateRepositoryAsync("newrepo", Arg.Any<GitHubRepositoryCreate>(), Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubCreatedRepository>.Failure(gitHubError, reason, 422));
        var service = CreateService();

        var result = await service.CreateAsync(
            new RepositoryCreateRequest("newrepo", "owner", "NewRepo", null, null, LocalRoot),
            TestContext.Current.CancellationToken);

        result.Error.Should().Be(expected);
        result.Diagnostic.Should().Be(reason);
        _tokenStore.Deleted.Should().Contain("newrepo");
        await _git.DidNotReceive().AddRemoteAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _store.DidNotReceive().SaveRepositoryAsync(
            Arg.Any<string>(), Arg.Any<RepositoryOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_ApprovalDenied_DoesNotCallGitHubOrPromptToken()
    {
        _approval.RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalPromptOutcome.Denied());
        var service = CreateService();

        var result = await service.CreateAsync(
            new RepositoryCreateRequest("newrepo", "owner", "NewRepo", null, null, null),
            TestContext.Current.CancellationToken);

        result.Error.Should().Be(RepositoryRegistrationError.ApprovalDenied);
        await _tokenPrompt.DidNotReceive().RequestTokenAsync(Arg.Any<TokenPromptRequest>(), Arg.Any<CancellationToken>());
        await _gitHub.DidNotReceive().CreateRepositoryAsync(
            Arg.Any<string>(), Arg.Any<GitHubRepositoryCreate>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_OriginAlreadyConfigured_RejectsBeforeApproval()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            new RepositoryCreateRequest("newrepo", "owner", "NewRepo", null, null, LocalRoot),
            TestContext.Current.CancellationToken);

        result.Error.Should().Be(RepositoryRegistrationError.RemoteAlreadyConfigured);
        await _approval.DidNotReceive().RequestApprovalAsync(
            Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_TokenNotEntered_ReturnsTokenUnavailable()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            new RepositoryCreateRequest("newrepo", "owner", "NewRepo", null, null, null),
            TestContext.Current.CancellationToken);

        result.Error.Should().Be(RepositoryRegistrationError.TokenUnavailable);
        await _gitHub.DidNotReceive().GetAuthenticatedUserLoginAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_StoredToken_IsUsedWithoutPromptAndKeptOnFailure()
    {
        _tokenStore.Stored = "stored-token";
        ArrangeLogin("owner");
        _gitHub.CreateRepositoryAsync("newrepo", Arg.Any<GitHubRepositoryCreate>(), Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubCreatedRepository>.Failure(GitHubError.RepositoryAlreadyExists));
        var service = CreateService();

        var result = await service.CreateAsync(
            new RepositoryCreateRequest("newrepo", "owner", "NewRepo", null, null, null),
            TestContext.Current.CancellationToken);

        result.Error.Should().Be(RepositoryRegistrationError.GitHubRepositoryAlreadyExists);
        await _tokenPrompt.DidNotReceive().RequestTokenAsync(Arg.Any<TokenPromptRequest>(), Arg.Any<CancellationToken>());
        _tokenStore.Deleted.Should().BeEmpty();
    }

    [Theory]
    [InlineData("-owner", "repo", null, RepositoryRegistrationError.InvalidGitHubName)]
    [InlineData("owner", "bad name", null, RepositoryRegistrationError.InvalidGitHubName)]
    [InlineData("owner", "..", null, RepositoryRegistrationError.InvalidGitHubName)]
    [InlineData("owner", "repo", "internal", RepositoryRegistrationError.InvalidVisibility)]
    public async Task CreateAsync_InvalidInput_RejectsBeforeApproval(
        string owner, string name, string? visibility, RepositoryRegistrationError expected)
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            new RepositoryCreateRequest("newrepo", owner, name, visibility, null, null),
            TestContext.Current.CancellationToken);

        result.Error.Should().Be(expected);
        await _approval.DidNotReceive().RequestApprovalAsync(
            Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    private void ArrangeLocalRootWithoutOrigin()
    {
        _git.GetRemoteUrlAsync(LocalRoot, "origin", Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Failed(GitCommandFailure.Failed));
        _git.AddRemoteAsync(LocalRoot, "origin", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(GitCommandResult.Success(string.Empty));
    }

    private void ArrangeTokenEntered() =>
        _tokenPrompt.RequestTokenAsync(Arg.Any<TokenPromptRequest>(), Arg.Any<CancellationToken>())
            .Returns(InteractiveTokenPromptResult.Accepted("entered-token".ToCharArray()));

    private void ArrangeLogin(string login) =>
        _gitHub.GetAuthenticatedUserLoginAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(GitHubResult<string>.Success(login));

    private void ArrangeCreated(bool isPrivate, string owner = "owner") =>
        _gitHub.CreateRepositoryAsync(Arg.Any<string>(), Arg.Any<GitHubRepositoryCreate>(), Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubCreatedRepository>.Success(new GitHubCreatedRepository(
                owner, "NewRepo", isPrivate, $"https://github.com/{owner}/NewRepo", $"https://github.com/{owner}/NewRepo.git")));

    private RepositoryRegistrationService CreateService() => new(
        _allowlist,
        new LocalPathValidator(_environment),
        _git,
        _approval,
        _store,
        _tokenPrompt,
        _tokenStore,
        _gitHub);

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

        public string? Stored { get; set; }

        public List<string> Deleted { get; } = [];

        public ApiTokenStoreReadResult Read(string repositoryId) =>
            Stored is null
                ? ApiTokenStoreReadResult.Failure(ApiTokenStoreError.TokenNotFound)
                : ApiTokenStoreReadResult.Success(Stored.ToCharArray());

        public ApiTokenStoreResult Delete(string repositoryId)
        {
            Deleted.Add(repositoryId);
            return ApiTokenStoreResult.Success();
        }

        public ApiTokenStoreResult Rename(string oldRepositoryId, string newRepositoryId) =>
            ApiTokenStoreResult.Success();
    }
}
