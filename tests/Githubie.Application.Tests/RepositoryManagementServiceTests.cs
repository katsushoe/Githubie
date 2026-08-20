using FluentAssertions;
using Githubie.Application.Configuration;
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
    private readonly RepositoryAllowlist _allowlist = new(new Dictionary<string, RepositoryOptions>
    {
        [RepositoryId] = CreateOptions(),
    });

    public RepositoryManagementServiceTests() =>
        _approval.RequestApprovalAsync(Arg.Any<ApprovalPromptRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalPromptOutcome.Approved());

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

    private RepositoryManagementService CreateService() => new(_allowlist, _approval, _store);

    private static RepositoryOptions CreateOptions() => new(
        "owner", "repo", "C:\\repo", "origin", "develop", "main",
        ["develop"], ["develop", "main"], ["main"], "main",
        "^v[0-9]+\\.[0-9]+\\.[0-9]+.*$", "merge", true);
}
