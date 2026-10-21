using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.Credentials;
using Githubie.Application.Interactive;
using Githubie.Application.Repositories;
using Githubie.Domain;
using NSubstitute;
using Xunit;

namespace Githubie.Application.Tests;

public sealed class RepositoryManagementServiceTests
{
    private const string RepositoryId = "sample";
    private readonly IInteractiveApprovalPrompt _approval = Substitute.For<IInteractiveApprovalPrompt>();
    private readonly IRepositoryConfigurationStore _store = Substitute.For<IRepositoryConfigurationStore>();
    private readonly IApiTokenStore _tokens = Substitute.For<IApiTokenStore>();
    private readonly RepositoryAllowlist _allowlist = new(new Dictionary<string, RepositoryOptions>
    {
        [RepositoryId] = CreateOptions(),
    });

    public RepositoryManagementServiceTests()
    {
        _approval.RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalPromptOutcome.Approved());
        _tokens.Rename(Arg.Any<string>(), Arg.Any<string>()).Returns(ApiTokenStoreResult.Success());
    }

    [Fact]
    public async Task UpdateAsync_AuthorIdentity_PersistsPerRepository()
    {
        var service = CreateService();
        var request = new RepositoryUpdateRequest(
            ["develop"], ["develop", "main"], ["main"], "main", "^v[0-9]+$", true,
            CommitAuthorName: "Writer", CommitAuthorEmail: "writer@example.com");

        var result = await service.UpdateAsync(RepositoryId, request, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        _allowlist.TryGet(RepositoryId, out var updated).Should().BeTrue();
        updated.CommitAuthorName.Should().Be("Writer");
        updated.CommitAuthorEmail.Should().Be("writer@example.com");
    }

    [Fact]
    public Task SettingsRead_ReturnsAllowlistedFieldsAndAuthorNull()
    {
        var service = CreateService();
        service.TryGetSettings(RepositoryId, out var settings).Should().BeTrue();
        settings.CommitAuthorName.Should().BeNull();
        settings.CommitAuthorEmail.Should().BeNull();
        settings.DirectPushBranches.Should().Equal("develop");
        return Task.CompletedTask;
    }

    [Fact]
    public void LegacyRepositoryOptionsWithoutPullRequestRoutes_KeepConfiguredDefaultRoute()
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(CreateOptions()))!.AsObject();
        json.Remove("PullRequestRoutes");
        var migrated = JsonSerializer.Deserialize<RepositoryOptions>(json.ToJsonString())!;
        migrated.PullRequestRoutes.Should().Equal(new PullRequestRoute("develop", "main"));
    }

    [Fact]
    public async Task SettingsRead_UnknownRepositoryAndUpdate_ReturnSpecificNotRegisteredResult()
    {
        var service = CreateService();
        service.TryGetSettings("missing", out _).Should().BeFalse();
        var result = await service.UpdateSettingsAsync("missing",
            new RepositorySettingsChanges(RequireCleanWorkingTree: false, HasRequireCleanWorkingTree: true), TestContext.Current.CancellationToken);
        result.Error.Should().Be(RepositoryMutationError.RepositoryNotRegistered);
        await _store.DidNotReceive().SaveRepositoryAsync(Arg.Any<string>(), Arg.Any<RepositoryOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SettingsUpdate_AuthorOnly_PreservesAllOtherRegisteredValues()
    {
        _allowlist.TryGet(RepositoryId, out var original).Should().BeTrue();
        var mapped = original with
        {
            MoyaiProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Workflows = new Dictionary<string, WorkflowPolicyOptions>
            {
                ["release.yml"] = new(["main"], new Dictionary<string, WorkflowInputPolicyOptions>()),
            },
        };
        _allowlist.TryUpdate(RepositoryId, mapped).Should().BeTrue();
        var service = CreateService();
        var result = await service.UpdateSettingsAsync(RepositoryId,
            new RepositorySettingsChanges(CommitAuthorName: "Writer", HasCommitAuthorName: true,
                CommitAuthorEmail: "writer@example.com", HasCommitAuthorEmail: true), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        _allowlist.TryGet(RepositoryId, out var updated).Should().BeTrue();
        updated.CommitAuthorName.Should().Be("Writer");
        updated.DirectPushBranches.Should().Equal("develop");
        updated.PullBranches.Should().Equal("develop", "main");
        updated.ProtectedBranches.Should().Equal("main");
        updated.TagPattern.Should().Be("^v[0-9]+\\.[0-9]+\\.[0-9]+.*$");
        updated.RequireCleanWorkingTree.Should().BeTrue();
        updated.GitHubOwner.Should().Be(mapped.GitHubOwner);
        updated.GitHubRepo.Should().Be(mapped.GitHubRepo);
        updated.LocalRoot.Should().Be(mapped.LocalRoot);
        updated.Remote.Should().Be(mapped.Remote);
        updated.MoyaiProjectId.Should().Be(mapped.MoyaiProjectId);
        updated.Workflows.Should().BeEquivalentTo(mapped.Workflows);
    }

    [Fact]
    public async Task SettingsUpdate_ExplicitFalseAndEmptyArray_ArePreserved()
    {
        var service = CreateService();
        var result = await service.UpdateSettingsAsync(RepositoryId,
            new RepositorySettingsChanges(DirectPushBranches: [], HasDirectPushBranches: true,
                RequireCleanWorkingTree: false, HasRequireCleanWorkingTree: true), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        _allowlist.TryGet(RepositoryId, out var updated).Should().BeTrue();
        updated.DirectPushBranches.Should().BeEmpty();
        updated.RequireCleanWorkingTree.Should().BeFalse();
    }

    [Fact]
    public async Task SettingsUpdate_PullRequestRoutes_ArePersistedAndReturned()
    {
        var service = CreateService();
        var routes = new[] { new PullRequestRoute("release", "main") };

        var result = await service.UpdateSettingsAsync(RepositoryId,
            new RepositorySettingsChanges(PullRequestRoutes: routes, HasPullRequestRoutes: true), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        _allowlist.TryGet(RepositoryId, out var updated).Should().BeTrue();
        updated.PullRequestRoutes.Should().Equal(routes);
        result.Value!.Settings!.PullRequestRoutes.Should().Equal(routes);
    }

    [Fact]
    public async Task SettingsUpdate_InvalidAuthorPair_DoesNotMutate()
    {
        var service = CreateService();
        var result = await service.UpdateSettingsAsync(RepositoryId,
            new RepositorySettingsChanges(CommitAuthorName: "Writer", HasCommitAuthorName: true), TestContext.Current.CancellationToken);

        result.Error.Should().Be(RepositoryMutationError.InvalidAuthorIdentity);
        await _store.DidNotReceive().SaveRepositoryAsync(Arg.Any<string>(), Arg.Any<RepositoryOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SettingsUpdate_Denied_DoesNotPersistOrChangeAllowlist()
    {
        _approval.RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalPromptOutcome.Denied());
        var service = CreateService();
        var result = await service.UpdateSettingsAsync(RepositoryId,
            new RepositorySettingsChanges(CommitAuthorName: "Writer", HasCommitAuthorName: true,
                CommitAuthorEmail: "writer@example.com", HasCommitAuthorEmail: true), TestContext.Current.CancellationToken);

        result.Error.Should().Be(RepositoryMutationError.ApprovalDenied);
        _allowlist.TryGet(RepositoryId, out var unchanged).Should().BeTrue();
        unchanged.CommitAuthorName.Should().BeNull();
        await _store.DidNotReceive().SaveRepositoryAsync(Arg.Any<string>(), Arg.Any<RepositoryOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_Approved_PersistsAndUpdatesAllowlist()
    {
        var service = CreateService();
        var request = new RepositoryUpdateRequest(
            ["develop", "release"], ["develop", "main"], ["main"], "main", "^v[0-9]+$", false);

        var result = await service.UpdateAsync("SAMPLE", request, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        await _store.Received(1).SaveRepositoryAsync(
            RepositoryId, Arg.Is<RepositoryOptions>(x => x.DirectPushBranches.SequenceEqual(request.DirectPushBranches)
                && x.TagPattern == request.TagPattern && !x.RequireCleanWorkingTree), Arg.Any<CancellationToken>());
        _allowlist.TryGet(RepositoryId, out var updated).Should().BeTrue();
        updated.DirectPushBranches.Should().Equal("develop", "release");
    }

    [Fact]
    public async Task UpdateAsync_Denied_DoesNotMutate()
    {
        _approval.RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalPromptOutcome.Denied());
        var service = CreateService();

        var result = await service.UpdateAsync(RepositoryId,
            new RepositoryUpdateRequest(["release"], ["main"], ["main"], "main", "^v", true),
            TestContext.Current.CancellationToken);

        result.Error.Should().Be(RepositoryMutationError.ApprovalDenied);
        await _store.DidNotReceive().SaveRepositoryAsync(
            Arg.Any<string>(), Arg.Any<RepositoryOptions>(), Arg.Any<CancellationToken>());
        _allowlist.TryGet(RepositoryId, out var unchanged).Should().BeTrue();
        unchanged.DirectPushBranches.Should().Equal("develop");
    }

    [Fact]
    public async Task UnregisterAsync_RemovesWithoutApproval()
    {
        var service = CreateService();

        var result = await service.UnregisterAsync(RepositoryId, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Approved.Should().BeFalse();
        await _store.Received(1).DeleteRepositoryAsync(RepositoryId, Arg.Any<CancellationToken>());
        await _approval.DidNotReceive().RequestApprovalAsync(
            Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        _allowlist.TryGet(RepositoryId, out _).Should().BeFalse();
    }

    [Fact]
    public async Task UnregisterAsync_UnknownRepository_ReturnsSpecificError()
    {
        var service = CreateService();

        var result = await service.UnregisterAsync("missing", TestContext.Current.CancellationToken);

        result.Error.Should().Be(RepositoryMutationError.RepositoryNotRegistered);
        await _store.DidNotReceive().DeleteRepositoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RenameAsync_ValidRequest_MigratesTokenConfigurationAndAllowlist()
    {
        var service = CreateService();

        var result = await service.RenameAsync("SAMPLE", "Renamed", TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        _tokens.Received(1).Rename(RepositoryId, "renamed");
        await _store.Received(1).RenameRepositoryAsync(RepositoryId, "renamed", Arg.Any<CancellationToken>());
        _allowlist.TryGet(RepositoryId, out _).Should().BeFalse();
        _allowlist.TryGet("renamed", out _).Should().BeTrue();
    }

    [Fact]
    public async Task RenameAsync_MissingToken_LeavesConfigurationAndAllowlistUnchanged()
    {
        _tokens.Rename(RepositoryId, "renamed")
            .Returns(ApiTokenStoreResult.Failure(ApiTokenStoreError.TokenNotFound));
        var service = CreateService();

        var result = await service.RenameAsync(RepositoryId, "renamed", TestContext.Current.CancellationToken);

        result.Error.Should().Be(RepositoryMutationError.TokenNotFound);
        await _store.DidNotReceive().RenameRepositoryAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _allowlist.TryGet(RepositoryId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task RenameAsync_ConfigurationFailure_RollsTokenBack()
    {
        _store.RenameRepositoryAsync(RepositoryId, "renamed", Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new IOException("test"));
        var service = CreateService();

        var result = await service.RenameAsync(RepositoryId, "renamed", TestContext.Current.CancellationToken);

        result.Error.Should().Be(RepositoryMutationError.PersistenceFailed);
        _tokens.Received(1).Rename("renamed", RepositoryId);
        _allowlist.TryGet(RepositoryId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task SetMoyaiProjectAsync_Approved_PersistsAndAppliesWithoutRestart()
    {
        var project = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var mapping = new MoyaiProjectMapping(_allowlist, null);
        var service = new RepositoryManagementService(_allowlist, _approval, _store, _tokens, mapping);

        var result = await service.SetMoyaiProjectAsync("SAMPLE", project, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        await _store.Received(1).SaveRepositoryAsync(
            RepositoryId, Arg.Is<RepositoryOptions>(x => x.MoyaiProjectId == project), Arg.Any<CancellationToken>());
        mapping.Find(RepositoryId).Should().Be(project);
    }

    [Fact]
    public async Task SetMoyaiProjectAsync_UnregisteredRepository_IsRejected()
    {
        var service = CreateService();

        var result = await service.SetMoyaiProjectAsync("missing", Guid.Parse("11111111-1111-1111-1111-111111111111"), TestContext.Current.CancellationToken);

        result.Error.Should().Be(RepositoryMutationError.RepositoryNotRegistered);
        await _approval.DidNotReceive().RequestApprovalAsync(
            Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("bad id")]
    [InlineData("-sample")]
    public async Task SetMoyaiProjectAsync_InvalidRepositoryId_IsRejected(string repositoryId)
    {
        var service = CreateService();

        var result = await service.SetMoyaiProjectAsync(repositoryId, Guid.Parse("11111111-1111-1111-1111-111111111111"), TestContext.Current.CancellationToken);

        result.Error.Should().Be(RepositoryMutationError.InvalidRepositoryId);
    }

    [Fact]
    public async Task SetMoyaiProjectAsync_EmptyUuid_IsRejected()
    {
        var service = CreateService();

        var result = await service.SetMoyaiProjectAsync(RepositoryId, Guid.Empty, TestContext.Current.CancellationToken);

        result.Error.Should().Be(RepositoryMutationError.InvalidMoyaiProjectId);
    }

    [Fact]
    public async Task SetMoyaiProjectAsync_UuidUsedByAnotherRepository_IsRejectedBeforeApproval()
    {
        var project = Guid.Parse("11111111-1111-1111-1111-111111111111");
        _allowlist.TryAdd("other", CreateOptions() with { MoyaiProjectId = project });
        var service = CreateService();

        var result = await service.SetMoyaiProjectAsync(RepositoryId, project, TestContext.Current.CancellationToken);

        result.Error.Should().Be(RepositoryMutationError.DuplicateMoyaiProjectId);
        await _approval.DidNotReceive().RequestApprovalAsync(
            Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetMoyaiProjectAsync_DifferentFromAdministratorMapping_IsRejected()
    {
        var mapping = new MoyaiProjectMapping(_allowlist, new Dictionary<string, Guid> { [RepositoryId] = Guid.Parse("22222222-2222-2222-2222-222222222222") });
        var service = new RepositoryManagementService(_allowlist, _approval, _store, _tokens, mapping);

        var result = await service.SetMoyaiProjectAsync(RepositoryId, Guid.Parse("11111111-1111-1111-1111-111111111111"), TestContext.Current.CancellationToken);

        result.Error.Should().Be(RepositoryMutationError.DuplicateMoyaiProjectId);
        mapping.Find(RepositoryId).Should().Be(Guid.Parse("22222222-2222-2222-2222-222222222222"));
    }

    [Fact]
    public async Task SetMoyaiProjectAsync_ApprovalDenied_DoesNotPersist()
    {
        _approval.RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalPromptOutcome.Denied());
        var service = CreateService();

        var result = await service.SetMoyaiProjectAsync(RepositoryId, Guid.Parse("11111111-1111-1111-1111-111111111111"), TestContext.Current.CancellationToken);

        result.Error.Should().Be(RepositoryMutationError.ApprovalDenied);
        await _store.DidNotReceiveWithAnyArgs().SaveRepositoryAsync(default!, default!, TestContext.Current.CancellationToken);
        _allowlist.TryGet(RepositoryId, out var unchanged).Should().BeTrue();
        unchanged.MoyaiProjectId.Should().BeNull();
    }

    private RepositoryManagementService CreateService() => new(_allowlist, _approval, _store, _tokens);

    private static RepositoryOptions CreateOptions() => new(
        "owner", "repo", "C:\\repo", "origin", "develop", "main",
        ["develop"], ["develop", "main"], ["main"], "main",
        "^v[0-9]+\\.[0-9]+\\.[0-9]+.*$", "merge", true);
}
