using System.Text.Json.Nodes;
using FluentAssertions;
using Githubie.Application.GitHub;
using Githubie.Infrastructure.GitHub;
using Xunit;

namespace Githubie.Infrastructure.Tests;

public sealed class GitHubBranchProtectionPayloadTests
{
    private const string ExistingRule = """
        {
          "url":"https://example.invalid/rule",
          "required_status_checks":{"strict":true,"contexts":["build"],"checks":[{"context":"build","app_id":123}]},
          "required_pull_request_reviews":{"dismiss_stale_reviews":true,"require_code_owner_reviews":true,"required_approving_review_count":2,"require_last_push_approval":true,
            "dismissal_restrictions":{"users":[{"login":"reviewer"}],"teams":[{"slug":"maintainers"}],"apps":[]}},
          "restrictions":{"users":[{"login":"writer"}],"teams":[{"slug":"release"}],"apps":[]},
          "required_signatures":{"enabled":false},"enforce_admins":{"enabled":true},
          "required_linear_history":{"enabled":true},"allow_force_pushes":{"enabled":false},
          "allow_deletions":{"enabled":false},"block_creations":{"enabled":true},
          "required_conversation_resolution":{"enabled":true},"lock_branch":{"enabled":false},
          "allow_fork_syncing":{"enabled":false}
        }
        """;

    [Fact]
    public void Update_PreservesUnrelatedPoliciesAndAppBinding()
    {
        var current = JsonNode.Parse(ExistingRule)!.AsObject();
        var original = current.ToJsonString();

        var valid = GitHubBranchProtectionPayload.TryBuild(current, new(AllowDeletions: true), out var payload);

        valid.Should().BeTrue();
        payload!["allow_deletions"]!.GetValue<bool>().Should().BeTrue();
        payload["allow_force_pushes"]!.GetValue<bool>().Should().BeFalse();
        payload["required_linear_history"]!.GetValue<bool>().Should().BeTrue();
        payload["required_conversation_resolution"]!.GetValue<bool>().Should().BeTrue();
        payload["required_status_checks"]!["checks"]![0]!["app_id"]!.GetValue<long>().Should().Be(123);
        payload["required_pull_request_reviews"]!["dismissal_restrictions"]!["users"]![0]!.GetValue<string>().Should().Be("reviewer");
        payload["restrictions"]!["teams"]![0]!.GetValue<string>().Should().Be("release");
        current.ToJsonString().Should().Be(original);
    }

    [Fact]
    public void UnknownSecurityField_RejectsWithoutReturningPartialPayload()
    {
        var current = JsonNode.Parse(ExistingRule)!.AsObject();
        current["future_security_requirement"] = new JsonObject { ["enabled"] = true };
        GitHubBranchProtectionPayload.TryBuild(current, new(AllowDeletions: true), out var payload).Should().BeFalse();
        payload.Should().BeNull();
    }

    [Fact]
    public void UnknownNestedReviewField_IsRejected()
    {
        var current = JsonNode.Parse(ExistingRule)!.AsObject();
        current["required_pull_request_reviews"]!["future_rule"] = true;
        GitHubBranchProtectionPayload.TryBuild(current, new(AllowDeletions: true), out _).Should().BeFalse();
    }

    [Fact]
    public void ExistingSignaturesOrMissingPolicyEvidence_AreRejected()
    {
        var current = JsonNode.Parse(ExistingRule)!.AsObject();
        current["required_signatures"]!["enabled"] = true;
        GitHubBranchProtectionPayload.TryBuild(current, new(AllowDeletions: true), out _).Should().BeFalse();
        current = JsonNode.Parse(ExistingRule)!.AsObject();
        current.Remove("lock_branch");
        GitHubBranchProtectionPayload.TryBuild(current, new(AllowDeletions: true), out _).Should().BeFalse();
    }

    [Fact]
    public void NewRule_RequiresExplicitRequiredPolicyFields_AndEmitsNulls()
    {
        GitHubBranchProtectionPayload.TryBuild(null, new(AllowDeletions: false), out _).Should().BeFalse();
        var edit = new GitHubBranchProtectionEdit(RequirePullRequest: false, AllowForcePushes: false,
            AllowDeletions: false, EnforceAdmins: true, RequireStatusChecks: false);

        GitHubBranchProtectionPayload.TryBuild(null, edit, out var payload).Should().BeTrue();
        payload!.ContainsKey("required_status_checks").Should().BeTrue();
        payload["required_status_checks"].Should().BeNull();
        payload.ContainsKey("required_pull_request_reviews").Should().BeTrue();
        payload["restrictions"].Should().BeNull();
        payload["enforce_admins"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void ContextReplacement_DoesNotSilentlyRemoveExistingAppBinding()
    {
        var current = JsonNode.Parse(ExistingRule)!.AsObject();
        GitHubBranchProtectionPayload.TryBuild(current, new(StatusCheckContexts: ["new-build"]), out var rejected).Should().BeFalse();
        rejected.Should().BeNull();
        current["required_status_checks"]!["checks"]![0]!["app_id"] = -1;
        GitHubBranchProtectionPayload.TryBuild(current, new(StatusCheckContexts: ["new-build"]), out var payload).Should().BeTrue();
        payload!["required_status_checks"]!["contexts"]![0]!.GetValue<string>().Should().Be("new-build");
        payload["required_status_checks"]!.AsObject().ContainsKey("checks").Should().BeFalse();
    }
}
