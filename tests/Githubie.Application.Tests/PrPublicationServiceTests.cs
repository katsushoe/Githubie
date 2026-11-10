using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.Git;
using Githubie.Application.GitHub;
using Githubie.Application.Interactive;
using Githubie.Application.Publishing;
using Githubie.Application.Repositories;
using Githubie.Domain;
using NSubstitute;
using Xunit;

namespace Githubie.Application.Tests;

public sealed class PrPublicationServiceTests
{
    private const string Sha = "1111111111111111111111111111111111111111";
    private const string OtherSha = "2222222222222222222222222222222222222222";
    private const string Source = "work/topic";
    private const string Destination = "review-target";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Publish_MissingRemote_CreatesFixedShaAndMatchingPrWithDurableStages()
    {
        var f = new Fixture();
        var result = await f.Run();
        result.IsSuccess.Should().BeTrue();
        result.Data.PublicationState.Should().Be("created");
        result.Data.PullRequestState.Should().Be("created");
        result.Data.PullRequestHeadSha.Should().Be(Sha);
        f.Pushes.Should().Be(1);
        f.Posts.Should().Be(1);
        f.PublicationWasJournaled.Should().BeTrue();
        f.PrWasJournaled.Should().BeTrue();
        f.Options.DirectPushBranches.Should().Equal("ordinary-push");
    }

    [Fact]
    public async Task Publish_ExistingEqualRemote_DoesNotPush()
    {
        var f = new Fixture { RemoteSha = Sha };
        (await f.Run()).Data.PublicationState.Should().Be("already_present");
        f.Pushes.Should().Be(0);
        f.Posts.Should().Be(1);
    }

    [Fact]
    public async Task Publish_ExistingDifferentRemote_RejectsBeforeApprovalOrMutation()
    {
        var f = new Fixture { RemoteSha = OtherSha };
        var result = await f.Run();
        result.ErrorCode.Should().Be("remote_branch_conflict");
        result.Outcome.Should().Be("not_executed");
        f.Approvals.Should().Be(0);
        f.Pushes.Should().Be(0);
        f.Posts.Should().Be(0);
    }

    [Theory]
    [InlineData("", "publication_invalid")]
    [InlineData("refs/heads/work/topic", "publication_invalid")]
    [InlineData("../topic", "publication_invalid")]
    [InlineData("topic~1", "publication_invalid")]
    [InlineData("topic.lock", "publication_invalid")]
    [InlineData("-topic", "publication_invalid")]
    [InlineData("topic\nother", "publication_invalid")]
    [InlineData("topic//other", "publication_invalid")]
    [InlineData(".hidden/topic", "publication_invalid")]
    [InlineData("Work/Topic", "publication_source_not_allowed")]
    public async Task Publish_InvalidOrUnpermittedSource_NoMutation(string branch, string error)
    {
        var f = new Fixture();
        (await f.Run(f.Input with { SourceBranch = branch })).ErrorCode.Should().Be(error);
        f.Pushes.Should().Be(0);
        f.Posts.Should().Be(0);
    }

    [Fact]
    public async Task Publish_RouteAbsent_DirectPushPermissionDoesNotAuthorizePublication()
    {
        var f = new Fixture();
        f.Update(f.Options with { PullRequestRoutes = [], DirectPushBranches = [Source] });
        (await f.Run()).ErrorCode.Should().Be("pull_request_route_not_allowed");
        f.Pushes.Should().Be(0);
    }

    [Fact]
    public async Task Publish_ProtectedSource_Rejects()
    {
        var f = new Fixture();
        f.Update(f.Options with { ProtectedBranches = [Source] });
        (await f.Run()).ErrorCode.Should().Be("protected_branch");
        f.Pushes.Should().Be(0);
    }

    [Fact]
    public async Task Publish_UnpermittedRoute_Rejects()
    {
        var f = new Fixture();
        f.Update(f.Options with { PullRequestRoutes = [] });
        (await f.Run()).ErrorCode.Should().Be("pull_request_route_not_allowed");
        f.Pushes.Should().Be(0);
    }

    [Fact]
    public async Task Publish_ProtectedDestination_CanCreatePrWithoutUpdatingDestination()
    {
        var f = new Fixture();
        f.Update(f.Options with { ProtectedBranches = [Destination] });
        (await f.Run()).IsSuccess.Should().BeTrue();
        f.PushedBranch.Should().Be(Source);
    }

    [Fact]
    public async Task Publish_DirtyRequiredTree_Rejects()
    {
        var f = new Fixture { Dirty = true };
        (await f.Run()).ErrorCode.Should().Be("working_tree_dirty");
        f.Pushes.Should().Be(0);
    }

    [Fact]
    public async Task Publish_DirtyAllowedTree_OnlyExpectedCommitIsPublished()
    {
        var f = new Fixture { Dirty = true };
        f.Update(f.Options with { RequireCleanWorkingTree = false });
        (await f.Run()).IsSuccess.Should().BeTrue();
        f.PushedSha.Should().Be(Sha);
    }

    [Fact]
    public async Task Publish_SourceShaMismatch_Rejects()
    {
        var f = new Fixture { LocalSha = OtherSha };
        (await f.Run()).ErrorCode.Should().Be("expected_head_mismatch");
        f.Approvals.Should().Be(0);
    }

    [Fact]
    public async Task Publish_SourceMovesDuringApproval_RejectsBeforePublication()
    {
        var f = new Fixture();
        f.OnApproval = () => f.LocalSha = OtherSha;
        (await f.Run()).ErrorCode.Should().Be("expected_head_mismatch");
        f.Pushes.Should().Be(0);
    }

    [Fact]
    public async Task Publish_PolicyChangesDuringApproval_Rejects()
    {
        var f = new Fixture();
        f.OnApproval = () => f.Update(f.Options with { PullRequestRoutes = [] });
        (await f.Run()).ErrorCode.Should().Be("authorization_context_changed");
        f.Pushes.Should().Be(0);
    }

    [Theory]
    [InlineData(ApprovalOutcome.Denied, "approval_denied")]
    [InlineData(ApprovalOutcome.NoInteractiveSession, "approval_unavailable")]
    [InlineData(ApprovalOutcome.TimedOut, "approval_unavailable")]
    public async Task Publish_ApprovalRejected_NoExternalEffects(ApprovalOutcome outcome, string code)
    {
        var f = new Fixture { ApprovalOutcome = outcome };
        (await f.Run()).ErrorCode.Should().Be(code);
        f.Pushes.Should().Be(0);
        f.Posts.Should().Be(0);
    }

    [Theory]
    [InlineData("https://github.com/other/repo.git", "https://github.com/owner/repo.git")]
    [InlineData("https://github.com/owner/repo.git", "https://github.com/other/repo.git")]
    [InlineData("https://github.com/owner/repo.git", "https://github.com/owner/repo.git\nhttps://github.com/owner/repo.git")]
    public async Task Publish_MismatchedOrMultipleUrls_Rejects(string fetch, string push)
    {
        var f = new Fixture { FetchUrl = fetch, PushUrls = push };
        (await f.Run()).ErrorCode.Should().Be("provider_remote_mismatch");
        f.Pushes.Should().Be(0);
    }

    [Fact]
    public async Task Publish_RepositoryAuthenticationDenied_NoPublication()
    {
        var f = new Fixture { AccessError = GitHubError.AuthenticationFailed };
        (await f.Run()).ErrorCode.Should().Be("authentication_failed");
        f.Pushes.Should().Be(0);
    }

    [Fact]
    public async Task Publish_PrPermissionDenied_PreservesPublishedBranch()
    {
        var f = new Fixture { PostError = GitHubError.PermissionDenied };
        var result = await f.Run();
        result.ErrorCode.Should().Be("permission_denied");
        result.Data.PublicationState.Should().Be("created");
        result.Data.PullRequestState.Should().Be("failed");
        f.RemoteSha.Should().Be(Sha);
    }

    [Fact]
    public async Task Publish_PrTimeout_BlocksDifferentIdAndDoesNotRepeatPostOnReplay()
    {
        var f = new Fixture { PostError = GitHubError.Timeout };
        var first = await f.Run();
        first.Data.PullRequestState.Should().Be("unknown");
        first.Outcome.Should().Be("unknown");
        (await f.Run(f.Input with { OperationId = Guid.NewGuid().ToString("D") })).ErrorCode.Should().Be("source_operation_unresolved");
        (await f.Run()).Data.PullRequestState.Should().Be("unknown");
        f.Posts.Should().Be(1);
        f.Pushes.Should().Be(1);
    }

    [Fact]
    public async Task Status_PrAppearsAfterTimeout_ReconcilesWithoutMutating()
    {
        var f = new Fixture { PostError = GitHubError.Timeout };
        await f.Run();
        f.ExistingPr = Fixture.MatchingPr();
        var status = await f.Service().GetStatusAsync("sample", f.Input.OperationId, Ct);
        status.IsSuccess.Should().BeTrue();
        status.Data.PullRequestState.Should().Be("already_present");
        f.Posts.Should().Be(1);
        f.Pushes.Should().Be(1);
    }

    [Fact]
    public async Task Publish_ReplayedUnknownAfterRestart_PersistsReadReconciliationWithoutPost()
    {
        var f = new Fixture { PostError = GitHubError.Timeout };
        await f.Run();
        f.ExistingPr = Fixture.MatchingPr();
        (await f.Run()).IsSuccess.Should().BeTrue();
        f.Store.Records[f.Input.OperationId].Result.Data.RequiresReconciliation.Should().BeFalse();
        f.Posts.Should().Be(1);
    }

    [Fact]
    public async Task Publish_SameIdWithDifferentPayload_RejectsConflict()
    {
        var f = new Fixture();
        await f.Run();
        (await f.Run(f.Input with { Title = "different" })).ErrorCode.Should().Be("operation_conflict");
        f.Posts.Should().Be(1);
    }

    [Fact]
    public async Task Publish_UppercaseUuidAndShaReplay_DoesNotDuplicate()
    {
        var f = new Fixture();
        await f.Run();
        var result = await f.Run(f.Input with { OperationId = f.Input.OperationId.ToUpperInvariant(), ExpectedSourceSha = Sha.ToUpperInvariant() });
        result.IsSuccess.Should().BeTrue();
        f.Posts.Should().Be(1);
    }

    [Fact]
    public async Task Publish_PushTimeoutAndAbsentRead_RemainsUnknown()
    {
        var f = new Fixture { PushFailure = GitCommandFailure.TimedOut, SetRemoteOnPush = false };
        var result = await f.Run();
        result.ErrorCode.Should().Be("publication_result_unknown");
        result.Data.PublicationState.Should().Be("unknown");
        f.Posts.Should().Be(0);
        (await f.Run()).Data.PublicationState.Should().Be("unknown");
        f.Pushes.Should().Be(1);
    }

    [Fact]
    public async Task Publish_TransportFailureAndAbsentRead_DoesNotProveNoRemoteEffect()
    {
        var f = new Fixture { PushFailure = GitCommandFailure.Failed, SetRemoteOnPush = false };
        (await f.Run()).Data.PublicationState.Should().Be("unknown");
    }

    [Fact]
    public async Task Publish_CompetingWriterCreatesDifferentSha_DoesNotPostOrOverwrite()
    {
        var f = new Fixture { PushFailure = GitCommandFailure.Failed };
        f.OnPush = () => f.RemoteSha = OtherSha;
        var result = await f.Run();
        result.ErrorCode.Should().Be("remote_branch_conflict");
        f.RemoteSha.Should().Be(OtherSha);
        f.Posts.Should().Be(0);
    }

    [Fact]
    public async Task Publish_ExistingMatchingPr_ReusesWithoutPost()
    {
        var f = new Fixture { RemoteSha = Sha, ExistingPr = Fixture.MatchingPr() };
        (await f.Run()).Data.PullRequestState.Should().Be("already_present");
        f.Posts.Should().Be(0);
    }

    [Theory]
    [InlineData("head")]
    [InlineData("fork")]
    [InlineData("url")]
    public async Task Publish_MismatchedExistingPr_RejectsWithoutPost(string field)
    {
        var f = new Fixture { RemoteSha = Sha };
        var pr = Fixture.MatchingPr();
        f.ExistingPr = field switch
        {
            "head" => pr with { SourceCommitSha = OtherSha },
            "fork" => pr with { HeadRepository = "other/repo" },
            _ => pr with { Url = "https://evil.invalid/owner/repo/pull/7" },
        };
        (await f.Run()).ErrorCode.Should().Be("pull_request_head_mismatch");
        f.Posts.Should().Be(0);
    }

    [Fact]
    public async Task Publish_ReturnedPrWithWrongHead_RemainsUnknownWithPublicationKnown()
    {
        var f = new Fixture { CreatedPr = Fixture.MatchingPr() with { SourceCommitSha = OtherSha } };
        var result = await f.Run();
        result.ErrorCode.Should().Be("pull_request_head_mismatch");
        result.Data.PublicationState.Should().Be("created");
        result.Data.PullRequestState.Should().Be("unknown");
    }

    [Fact]
    public async Task Publish_GuardExpiresBeforePublication_NoMutation()
    {
        var f = new Fixture { ThrowOnGuard = 2 };
        var result = await f.Run();
        result.IsSuccess.Should().BeFalse();
        result.Data.PublicationState.Should().Be("not_attempted");
        result.Outcome.Should().Be("not_executed");
        f.Pushes.Should().Be(0);
        f.Posts.Should().Be(0);
    }

    [Fact]
    public async Task Publish_PrCancellation_DurableUnknownSurvivesServiceRestart()
    {
        var f = new Fixture { CancelPost = true };
        var result = await f.Run();
        result.Data.PullRequestState.Should().Be("unknown");
        (await f.Service().GetStatusAsync("sample", f.Input.OperationId, Ct)).Data.PullRequestState.Should().Be("unknown");
        f.Posts.Should().Be(1);
    }

    [Fact]
    public async Task Publish_JournalUnavailableBeforeDispatch_NoMutation()
    {
        var f = new Fixture();
        f.Store.FailWrites = true;
        (await f.Run()).IsSuccess.Should().BeFalse();
        f.Pushes.Should().Be(0);
        f.Posts.Should().Be(0);
    }

    [Fact]
    public async Task Publish_ConcurrentOperation_NoMutation()
    {
        var f = new Fixture();
        f.Store.AcquireAllowed = false;
        (await f.Run()).ErrorCode.Should().Be("operation_in_progress");
        f.Pushes.Should().Be(0);
    }

    [Fact]
    public async Task Status_UnknownOperation_DoesNotMutate()
    {
        var f = new Fixture();
        (await f.Service().GetStatusAsync("sample", f.Input.OperationId, Ct)).ErrorCode.Should().Be("operation_not_found");
        f.Pushes.Should().Be(0);
        f.Posts.Should().Be(0);
    }

    [Fact]
    public async Task OwnerMode_LegacyProjectChange_PreservesRecordedResultWithoutReplay()
    {
        var f = new Fixture { OwnerSid = "owner-one" };
        f.Update(f.Options with { MoyaiProjectId = Guid.NewGuid() });
        (await f.Run()).IsSuccess.Should().BeTrue();
        f.Update(f.Options with { MoyaiProjectId = Guid.NewGuid() });
        (await f.Run()).IsSuccess.Should().BeTrue();
        f.Pushes.Should().Be(1);
        f.Posts.Should().Be(1);
    }

    [Fact]
    public async Task Status_DifferentWindowsOwner_RejectsRecordedResult()
    {
        var f = new Fixture { OwnerSid = "owner-one" };
        (await f.Run()).IsSuccess.Should().BeTrue();
        f.OwnerSid = "owner-two";
        (await f.Service().GetStatusAsync("sample", f.Input.OperationId, Ct)).ErrorCode
            .Should().Be("authorization_context_changed");
        f.Posts.Should().Be(1);
    }

    [Fact]
    public async Task LegacyMode_ProjectChange_RejectsRecordedResult()
    {
        var f = new Fixture();
        f.Update(f.Options with { MoyaiProjectId = Guid.NewGuid() });
        (await f.Run()).IsSuccess.Should().BeTrue();
        f.Update(f.Options with { MoyaiProjectId = Guid.NewGuid() });
        (await f.Service().GetStatusAsync("sample", f.Input.OperationId, Ct)).ErrorCode
            .Should().Be("authorization_context_changed");
    }

    [Fact]
    public async Task Publish_LegacyLocalCommitSetting_DoesNotOverrideExplicitPrRoute()
    {
        var f = new Fixture();
        f.Update(f.Options with { LegacyLocalCommitBranches = [] });
        (await f.Run()).IsSuccess.Should().BeTrue();
        f.Pushes.Should().Be(1);
        f.Options.DirectPushBranches.Should().NotContain(Source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publish_RouteAllowedButSourceMissing_RejectsBeforeApprovalOrExternalAccess(bool legacyAllows)
    {
        var f = new Fixture();
        f.Update(f.Options with { PrPublicationSourceBranches = [], LegacyLocalCommitBranches = legacyAllows ? [Source] : [] });
        var result = await f.Run();
        result.ErrorCode.Should().Be("publication_source_not_allowed");
        result.Outcome.Should().Be("not_executed");
        f.Approvals.Should().Be(0);
        f.Pushes.Should().Be(0);
        f.Posts.Should().Be(0);
        await f.Api.DidNotReceive().GetRepositoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publish_SourceAllowedButRouteMissing_Rejects()
    {
        var f = new Fixture();
        f.Update(f.Options with { PullRequestRoutes = [] });
        var result = await f.Run();
        result.ErrorCode.Should().Be("pull_request_route_not_allowed");
        f.Approvals.Should().Be(0);
        f.Pushes.Should().Be(0);
        f.Posts.Should().Be(0);
    }

    [Fact]
    public async Task Publish_DifferentlyCasedSourceInList_RejectsDespiteExactRoute()
    {
        var f = new Fixture();
        f.Update(f.Options with { PrPublicationSourceBranches = [Source.ToUpperInvariant()] });
        (await f.Run()).ErrorCode.Should().Be("publication_source_not_allowed");
        f.Pushes.Should().Be(0);
        f.Posts.Should().Be(0);
    }

    [Fact]
    public async Task Publish_SourceRevokedDuringApproval_NoMutation()
    {
        var f = new Fixture();
        f.OnApproval = () => f.Update(f.Options with { PrPublicationSourceBranches = [] });
        (await f.Run()).ErrorCode.Should().Be("authorization_context_changed");
        f.Pushes.Should().Be(0);
        f.Posts.Should().Be(0);
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    public async Task Publish_SourceRevokedAtDispatchGuard_DoesNotDispatchNextMutation(int guardCall, int expectedPushes)
    {
        var f = new Fixture();
        f.OnGuard = count => { if (count == guardCall) f.Update(f.Options with { PrPublicationSourceBranches = [] }); };
        var result = await f.Run();
        result.ErrorCode.Should().Be("authorization_context_changed");
        result.Data.PublicationState.Should().Be(expectedPushes == 0 ? "not_attempted" : "created");
        result.Data.PullRequestState.Should().Be("not_attempted");
        result.Data.RequiresReconciliation.Should().BeFalse();
        f.Pushes.Should().Be(expectedPushes);
        f.Posts.Should().Be(0);
    }

    private sealed class Fixture
    {
        public readonly IGitCommandClient Git = Substitute.For<IGitCommandClient>();
        public readonly IPrPublicationGit Publication = Substitute.For<IPrPublicationGit>();
        public readonly IGitHubApiClient Api = Substitute.For<IGitHubApiClient>();
        public readonly IInteractiveApprovalPrompt Prompt = Substitute.For<IInteractiveApprovalPrompt>();
        public readonly IProviderExecutionGuard Guard = Substitute.For<IProviderExecutionGuard>();
        private readonly IRepositoryEnvironment _environment = Substitute.For<IRepositoryEnvironment>();
        public readonly MemoryStore Store = new();
        public readonly RepositoryAllowlist Allowlist;
        public RepositoryOptions Options;
        public string? OwnerSid;
        public readonly PrPublishInput Input = new(Source, Destination, Sha, "candidate", "body", false, Guid.NewGuid().ToString("D"));
        public string? RemoteSha;
        public string LocalSha = Sha;
        public bool Dirty, SetRemoteOnPush = true, CancelPost;
        public string FetchUrl = "https://github.com/owner/repo.git", PushUrls = "https://github.com/owner/repo.git";
        public GitCommandFailure? PushFailure;
        public GitHubError? PostError, AccessError;
        public ApprovalOutcome ApprovalOutcome = ApprovalOutcome.Approved;
        public GitHubPullRequestInfo? ExistingPr, CreatedPr;
        public Action? OnApproval, OnPush;
        public Action<int>? OnGuard;
        public int Pushes, Posts, Approvals, GuardCalls, ThrowOnGuard;
        public bool PublicationWasJournaled, PrWasJournaled;
        public string? PushedSha, PushedBranch;

        public Fixture()
        {
            Options = new("owner", "repo", "C:\\publication-fixture", "origin", "ordinary-push", Destination,
                ["ordinary-push"], [Destination], [Destination], Destination, "^v.*$", "merge", true)
            { PullRequestRoutes = [new PullRequestRoute(Source, Destination)], PrPublicationSourceBranches = [Source] };
            Allowlist = new(new Dictionary<string, RepositoryOptions> { ["sample"] = Options });
            _environment.GetFullPath(Arg.Any<string>()).Returns(c => c.Arg<string>());
            _environment.DirectoryExists(Arg.Any<string>()).Returns(true);
            _environment.GitMetadataExists(Arg.Any<string>()).Returns(true);
            _environment.ContainsReparsePoint(Arg.Any<string>()).Returns(false);
            Git.GetStatusAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => GitCommandResult.Success(Dirty ? " M file" : ""));
            Git.GetLocalCommitAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => GitCommandResult.Success(LocalSha));
            Git.GetRemoteUrlAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => GitCommandResult.Success(FetchUrl));
            Git.GetRemoteRefAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => GitCommandResult.Success(RemoteSha is null ? "" : RemoteSha + "\trefs/heads/" + Source));
            Publication.GetPushUrlsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => GitCommandResult.Success(PushUrls));
            Publication.CreateBranchAtCommitAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(c =>
                {
                    Pushes++;
                    PushedBranch = c.ArgAt<string>(3); PushedSha = c.ArgAt<string>(4);
                    PublicationWasJournaled = Store.Records[Input.OperationId].Result.Data.PublicationState == "unknown";
                    if (SetRemoteOnPush) RemoteSha = Sha;
                    OnPush?.Invoke();
                    return PushFailure is { } failure ? GitCommandResult.Failed(failure)
                        : GitCommandResult.Success("*\t" + Sha + ":refs/heads/" + Source + "\t[new branch]\n");
                });
            Api.GetRepositoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => AccessError is { } error ? GitHubResult<GitHubRepositoryInfo>.Failure(error)
                    : GitHubResult<GitHubRepositoryInfo>.Success(new("owner", "repo", Destination, null)));
            Api.GetBranchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(GitHubResult<GitHubBranchInfo>.Success(new(Destination, OtherSha, true)));
            Api.ListPullRequestsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<GitHubPullRequestState?>(),
                    Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(_ => GitHubResult<IReadOnlyList<GitHubPullRequestInfo>>.Success(ExistingPr is null ? [] : [ExistingPr]));
            Api.CreatePullRequestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<GitHubPullRequestCreate>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    Posts++;
                    PrWasJournaled = Store.Records[Input.OperationId].Result.Data.PullRequestState == "unknown";
                    if (CancelPost) throw new OperationCanceledException();
                    return PostError is { } error ? GitHubResult<GitHubPullRequestInfo>.Failure(error)
                        : GitHubResult<GitHubPullRequestInfo>.Success(CreatedPr ?? MatchingPr());
                });
            Prompt.RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
                .Returns(_ => { Approvals++; OnApproval?.Invoke(); return new ApprovalPromptOutcome(ApprovalOutcome); });
            Guard.EnsureCurrentAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            {
                GuardCalls++;
                OnGuard?.Invoke(GuardCalls);
                if (ThrowOnGuard == GuardCalls) throw new InvalidOperationException("expired");
                return Task.CompletedTask;
            });
        }

        public void Update(RepositoryOptions options) { Options = options; Allowlist.TryUpdate("sample", options).Should().BeTrue(); }
        public PrPublicationService Service() => new(Allowlist, new LocalPathValidator(_environment), Git, Publication, Api, Store,
            Prompt, Guard, new GithubieOptions(45460, "/mcp", new Dictionary<string, RepositoryOptions> { ["sample"] = Options })
            { WindowsOwnerSid = OwnerSid });
        public Task<PrPublicationResult> Run(PrPublishInput? input = null) => Service().PublishAsync("sample", input ?? Input, Ct);
        public static GitHubPullRequestInfo MatchingPr() => new(7, "candidate", null, GitHubPullRequestState.Open, Source, Destination,
            "writer", null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "https://github.com/owner/repo/pull/7",
            SourceCommitSha: Sha, HeadRepository: "owner/repo", BaseRepository: "owner/repo");
    }

    private sealed class MemoryStore : IPrPublicationStore
    {
        public readonly Dictionary<string, PrPublicationRecord> Records = new();
        public bool AcquireAllowed = true, FailWrites;
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IAsyncDisposable?> TryAcquireAsync(string repository, CancellationToken cancellationToken) =>
            Task.FromResult<IAsyncDisposable?>(AcquireAllowed ? new Lease() : null);
        public Task<PrPublicationRecord?> GetAsync(string repository, string operationId, CancellationToken cancellationToken) =>
            Task.FromResult(Records.GetValueOrDefault(operationId));
        public Task<bool> HasUnresolvedAsync(string repository, string sourceBranch, CancellationToken cancellationToken) =>
            Task.FromResult(Records.Values.Any(r => r.Result.Data.SourceBranch == sourceBranch && r.Result.Data.RequiresReconciliation));
        public Task PutAsync(string repository, string operationId, PrPublicationRecord record, CancellationToken cancellationToken)
        {
            if (FailWrites) throw new IOException("journal unavailable");
            Records[operationId] = record;
            return Task.CompletedTask;
        }
        private sealed class Lease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }
}
