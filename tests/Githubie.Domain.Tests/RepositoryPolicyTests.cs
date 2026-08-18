using FluentAssertions;
using Githubie.Domain;
using Xunit;

namespace Githubie.Domain.Tests;

public sealed class RepositoryPolicyTests
{
    private static RepositoryPolicy CreatePolicy(bool requireCleanWorkingTree = true) => new(
        RepositoryId: "example",
        DevelopBranch: "develop",
        MainBranch: "main",
        DirectPushBranches: new HashSet<string> { "develop" },
        PullBranches: new HashSet<string> { "develop", "main" },
        PullRequestRoutes: new HashSet<PullRequestRoute> { new("develop", "main") },
        ProtectedBranches: new HashSet<string> { "main" },
        TagTargetBranch: "main",
        TagPattern: "^v[0-9]+\\.[0-9]+\\.[0-9]+.*$",
        RequireCleanWorkingTree: requireCleanWorkingTree);

    [Fact]
    public void ValidatePush_AllowsCleanDevelopPush()
    {
        var policy = CreatePolicy();

        var result = policy.ValidatePush("develop", workingTreeClean: true);

        result.IsAllowed.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public void ValidatePush_DeniesProtectedBranch()
    {
        var policy = CreatePolicy();

        var result = policy.ValidatePush("main", workingTreeClean: true);

        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be(PolicyErrorCode.ProtectedBranch);
    }

    [Fact]
    public void ValidatePush_DeniesUnlistedBranch()
    {
        var policy = CreatePolicy();

        var result = policy.ValidatePush("feature/x", workingTreeClean: true);

        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be(PolicyErrorCode.BranchNotAllowed);
    }

    [Fact]
    public void ValidatePush_DeniesDirtyWorkingTreeWhenRequired()
    {
        var policy = CreatePolicy(requireCleanWorkingTree: true);

        var result = policy.ValidatePush("develop", workingTreeClean: false);

        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be(PolicyErrorCode.WorkingTreeDirty);
    }

    [Fact]
    public void ValidatePush_AllowsDirtyWorkingTreeWhenNotRequired()
    {
        var policy = CreatePolicy(requireCleanWorkingTree: false);

        var result = policy.ValidatePush("develop", workingTreeClean: false);

        result.IsAllowed.Should().BeTrue();
    }

    [Theory]
    [InlineData("develop", "main", true)]
    [InlineData("main", "develop", false)]
    [InlineData("main", "main", false)]
    [InlineData("unknown", "main", false)]
    [InlineData("develop", "unknown", false)]
    public void ValidatePullRequest_EnforcesAllowedRoutesOnly(string source, string destination, bool expectedAllowed)
    {
        var policy = CreatePolicy();

        var result = policy.ValidatePullRequest(source, destination);

        result.IsAllowed.Should().Be(expectedAllowed);
        if (!expectedAllowed)
        {
            result.ErrorCode.Should().Be(PolicyErrorCode.PullRequestRouteNotAllowed);
        }
    }

    [Fact]
    public void ValidateTag_DeniesNonMainTarget()
    {
        var policy = CreatePolicy();

        var result = policy.ValidateTag("v1.0.0", "develop");

        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be(PolicyErrorCode.TagTargetNotAllowed);
    }

    [Theory]
    [InlineData("v1.0.0", true)]
    [InlineData("v1.2.3-rc.1", true)]
    [InlineData("release-1", false)]
    [InlineData("1.0.0", false)]
    public void ValidateTag_EnforcesPatternOnMainTarget(string tag, bool expectedAllowed)
    {
        var policy = CreatePolicy();

        var result = policy.ValidateTag(tag, "main");

        result.IsAllowed.Should().Be(expectedAllowed);
        if (!expectedAllowed)
        {
            result.ErrorCode.Should().Be(PolicyErrorCode.TagInvalid);
        }
    }
}
