using System.Text.Json;
using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.Git;
using Githubie.Application.GitHub;
using Githubie.Application.Repositories;
using NSubstitute;
using Xunit;

namespace Githubie.Server.Tests;

public sealed class PrMergeProviderContractTests
{
    [Fact]
    public void LegacyMergeScope_IsDedicatedAndDoesNotChangeReadScope()
    {
        GithubieAssertionPolicy.TryGetScopes("github_pr_merge", out var merge).Should().BeTrue();
        merge.Should().Equal("provider.githubie.pull_request.merge");
        GithubieAssertionPolicy.TryGetScopes("github_pr_get", out var read).Should().BeTrue();
        read.Should().Equal("repository.read");
    }

    [Fact]
    public async Task OwnerCapability_AdvertisesPrReadAndMerge_AndPreservesOwnerContract()
    {
        var tools = Tools(Substitute.For<IGitHubRepositoryGateway>(), true);

        var result = await tools.GetProviderCapabilitiesAsync("sample", TestContext.Current.CancellationToken);

        result.Ok.Should().BeTrue();
        var json = JsonSerializer.SerializeToElement(result.Data, GithubieMcpJson.CreateOptions());
        json.GetProperty("pr_get").GetBoolean().Should().BeTrue();
        json.GetProperty("pr_merge").GetBoolean().Should().BeTrue();
        json.GetProperty("authentication").GetProperty("integration_mode").GetString().Should().Be("windows_owner");
        json.GetProperty("authentication").GetProperty("owner_authentication").GetProperty("assertion_required").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MergeTool_ForwardsExpectedSha_AndMarksOwnerRequirement(bool owner)
    {
        var gateway = Substitute.For<IGitHubRepositoryGateway>();
        gateway.MergePullRequestAsync("sample", Arg.Any<GitHubPullRequestMerge>(), Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubPullRequestInfo>.Failure(GitHubError.ExpectedSourceCommitRequired));
        var tools = Tools(gateway, owner);
        var sha = new string('a', 40);

        await tools.MergePullRequestAsync("sample", 1, null, null, TestContext.Current.CancellationToken, sha);

        await gateway.Received(1).MergePullRequestAsync("sample",
            Arg.Is<GitHubPullRequestMerge>(r => r.ExpectedSourceCommitSha == sha && r.RequireExpectedSourceCommit == owner), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void MergeTool_NewArgumentIsOptionalForLegacySchemaCompatibility()
    {
        var argument = typeof(GithubieMcpTools).GetMethod(nameof(GithubieMcpTools.MergePullRequestAsync))!
            .GetParameters().Single(p => p.Name == "expected_source_commit_sha");
        argument.IsOptional.Should().BeTrue();
        argument.DefaultValue.Should().BeNull();
    }

    [Theory]
    [InlineData(GitHubError.ExpectedSourceCommitMismatch, "expected_source_commit_mismatch", "not_executed")]
    [InlineData(GitHubError.PullRequestDraft, "pull_request_draft", "not_executed")]
    [InlineData(GitHubError.PullRequestRouteNotAllowed, "pull_request_route_not_allowed", "not_executed")]
    [InlineData(GitHubError.PullRequestMergeResultUnknown, "pr_merge_result_unknown", "unknown")]
    public void MergeErrors_KeepRoutingAndRecoveryFields(GitHubError source, string code, string outcome)
    {
        var result = GithubieToolResultMapper.Map("pr_merge", "sample", GitHubResult<string>.Failure(source));

        result.Ok.Should().BeFalse();
        result.Error!.Code.Should().Be(code);
        result.Error.Outcome.Should().Be(outcome);
        result.Error.Retryable.Should().BeFalse();
        result.Error.CorrelationId.Should().NotBeNullOrWhiteSpace();
        if (outcome == "unknown") result.Error.CommonCode.Should().Be("RESULT_UNKNOWN");
    }

    private static GithubieMcpTools Tools(IGitHubRepositoryGateway gateway, bool owner)
    {
        var repositories = new Dictionary<string, RepositoryOptions>
        {
            ["sample"] = new("owner", "repo", Path.GetTempPath(), "origin", "develop", "main",
                ["develop"], ["develop", "main"], ["main"], "main", "^v", "merge", true),
        };
        return new(Substitute.For<IGitGateway>(), gateway, Substitute.For<IRepositoryRegistrationService>(),
            Substitute.For<IRepositoryManagementService>(), new RepositoryAllowlist(repositories),
            new GithubieOptions(45460, "/mcp", repositories) { WindowsOwnerSid = owner ? "S-1-5-21-test" : null });
    }
}
