using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.Credentials;
using Githubie.Application.Interactive;
using Githubie.Application.Repositories;
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
    public async Task UpdateAsync_Approved_PersistsAndUpdatesAllowlist()
    {
        var service = CreateService();
        var request = new RepositoryUpdateRequest(
            ["develop", "release"], ["develop", "main"], ["main"], "main", "^v[0-9]+$", false);

        var result = await service.UpdateAsync(RepositoryId, request, TestContext.Current.CancellationToken);

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

        var result = await service.RenameAsync(RepositoryId, "renamed", TestContext.Current.CancellationToken);

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

    private RepositoryManagementService CreateService() => new(_allowlist, _approval, _store, _tokens);

    private static RepositoryOptions CreateOptions() => new(
        "owner", "repo", "C:\\repo", "origin", "develop", "main",
        ["develop"], ["develop", "main"], ["main"], "main",
        "^v[0-9]+\\.[0-9]+\\.[0-9]+.*$", "merge", true);
}
