using FluentAssertions;
using Githubie.Application.GitHub;
using Xunit;

namespace Githubie.Application.Tests;

public sealed class GitHubBranchProtectionEditTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("main*")]
    [InlineData("../main")]
    [InlineData("main/../other")]
    [InlineData("main\\other")]
    [InlineData("main.lock")]
    [InlineData("main//release")]
    [InlineData("@")]
    [InlineData("main@{1}")]
    [InlineData("/main")]
    [InlineData("main/")]
    public void BranchValidation_RejectsUnsafeOrWildcardNames(string? branch) =>
        GitHubProtectionBranchName.IsValid(branch).Should().BeFalse();

    [Theory]
    [InlineData("main")]
    [InlineData("release/2026.10")]
    [InlineData("feature/example")]
    public void BranchValidation_AcceptsExactBranchNames(string branch) =>
        GitHubProtectionBranchName.IsValid(branch).Should().BeTrue();

    [Fact]
    public void EmptyChange_IsRejected() => new GitHubBranchProtectionEdit().IsValid().Should().BeFalse();

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    public void ReviewCount_OutsideGitHubRange_IsRejected(int count) =>
        new GitHubBranchProtectionEdit(RequiredApprovingReviews: count).IsValid().Should().BeFalse();

    [Fact]
    public void DisablingReviewsWithAnApprovalCount_IsRejected() =>
        new GitHubBranchProtectionEdit(RequirePullRequest: false, RequiredApprovingReviews: 1).IsValid().Should().BeFalse();

    [Fact]
    public void DisablingChecksWhileSupplyingContexts_IsRejected() =>
        new GitHubBranchProtectionEdit(RequireStatusChecks: false, StatusCheckContexts: ["build"]).IsValid().Should().BeFalse();

    [Fact]
    public void EnablingChecksWithoutContexts_IsRejected() =>
        new GitHubBranchProtectionEdit(RequireStatusChecks: true).IsValid().Should().BeFalse();

    [Fact]
    public void DuplicateOrWhitespaceContexts_AreRejected()
    {
        new GitHubBranchProtectionEdit(StatusCheckContexts: ["build", "build"]).IsValid().Should().BeFalse();
        new GitHubBranchProtectionEdit(StatusCheckContexts: [" "]).IsValid().Should().BeFalse();
        new GitHubBranchProtectionEdit(StatusCheckContexts: ["build\nsecret"]).IsValid().Should().BeFalse();
    }

    [Fact]
    public void ExplicitSettingsAndReviewCount_AreAccepted() =>
        new GitHubBranchProtectionEdit(RequirePullRequest: true, RequiredApprovingReviews: 2,
            AllowForcePushes: false, AllowDeletions: false, EnforceAdmins: true,
            RequireStatusChecks: true, StatusCheckContexts: ["build"], StrictStatusChecks: true)
            .IsValid().Should().BeTrue();
}
