using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.Git;
using Githubie.Application.GitHub;
using Githubie.Application.Repositories;
using ModelContextProtocol.Server;
using NSubstitute;
using Xunit;

namespace Githubie.Server.Tests;

public sealed class GitHubBranchProtectionContractTests
{
    [Fact]
    public async Task CapabilitiesAndOwnerOperations_AdvertiseOnlyRegisteredService()
    {
        var gateway = Substitute.For<IGitHubBranchProtectionGateway>();
        var tools = Tools(gateway);
        var result = await tools.GetProviderCapabilitiesAsync("sample", TestContext.Current.CancellationToken);
        result.Ok.Should().BeTrue();
        result.Data!.BranchProtectionRead.Should().BeTrue();
        result.Data.BranchProtectionWrite.Should().BeTrue();
        result.Data.Authentication!.OwnerAuthentication!.Operations.Should().Contain(
            ["github_branch_protection_get", "github_branch_protection_update", "github_branch_protection_delete"]);
        var withoutService = await Tools(null).GetProviderCapabilitiesAsync("sample", TestContext.Current.CancellationToken);
        withoutService.Data!.BranchProtectionRead.Should().BeFalse();
        withoutService.Data.BranchProtectionWrite.Should().BeFalse();
    }

    [Fact]
    public void ToolSchemasAndLegacyScopes_MatchReadAndMutationContracts()
    {
        var tools = Tools(Substitute.For<IGitHubBranchProtectionGateway>());
        var get = McpServerTool.Create(typeof(GithubieMcpTools).GetMethod(nameof(GithubieMcpTools.GetBranchProtectionAsync))!, tools);
        var update = McpServerTool.Create(typeof(GithubieMcpTools).GetMethod(nameof(GithubieMcpTools.UpdateBranchProtectionAsync))!, tools);
        var delete = McpServerTool.Create(typeof(GithubieMcpTools).GetMethod(nameof(GithubieMcpTools.DeleteBranchProtectionAsync))!, tools);
        get.ProtocolTool.InputSchema.GetProperty("required").EnumerateArray().Select(item => item.GetString()).Should().Contain(["repository", "branch"]);
        update.ProtocolTool.InputSchema.GetProperty("required").EnumerateArray().Select(item => item.GetString()).Should().Contain(["repository", "branch", "edit", "create_if_missing"]);
        delete.ProtocolTool.Name.Should().Be("github_branch_protection_delete");
        GithubieAssertionPolicy.TryGetScopes(get.ProtocolTool.Name, out var read).Should().BeTrue();
        read.Should().Equal("repository.read");
        GithubieAssertionPolicy.TryGetScopes(update.ProtocolTool.Name, out var write).Should().BeTrue();
        write.Should().Equal("provider.githubie.branch_protection.write");
        GithubieAssertionPolicy.TryGetScopes(delete.ProtocolTool.Name, out var deleteScope).Should().BeTrue();
        deleteScope.Should().Equal("provider.githubie.branch_protection.write");
    }

    [Fact]
    public async Task Update_ForwardsExplicitChangesAndCreateFlag()
    {
        var gateway = Substitute.For<IGitHubBranchProtectionGateway>();
        gateway.UpdateAsync("sample", "main", Arg.Any<GitHubBranchProtectionEdit>(), true, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubBranchProtectionMutation>.Failure(GitHubError.BranchProtectionApprovalDenied));
        var result = await Tools(gateway).UpdateBranchProtectionAsync("sample", "main", new(EnforceAdmins: true), true, TestContext.Current.CancellationToken);
        result.Error!.Code.Should().Be("branch_protection_approval_denied");
        result.Error.Outcome.Should().Be("not_executed");
        await gateway.Received(1).UpdateAsync("sample", "main", Arg.Is<GitHubBranchProtectionEdit>(edit => edit.EnforceAdmins == true), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Audit_DoesNotReturnOrLogBackendDiagnostics()
    {
        var inner = Substitute.For<IGitHubBranchProtectionGateway>();
        var audit = Substitute.For<IGithubieAuditLogger>();
        inner.UpdateAsync("sample", "main", Arg.Any<GitHubBranchProtectionEdit>(), false, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubBranchProtectionMutation>.Failure(GitHubError.PermissionDenied, "dummy-token"));
        var wrapper = new AuditedGitHubBranchProtectionGateway(inner, audit);
        var result = await wrapper.UpdateAsync("sample", "main", new(EnforceAdmins: true), false, TestContext.Current.CancellationToken);
        result.Diagnostic.Should().BeNull();
        var response = GithubieToolResultMapper.Map("branch_protection_update", "sample", result);
        JsonSerializer.Serialize(response).Should().NotContain("dummy-token");
        response.Error!.Code.Should().Be("branch_protection_access_denied");
        response.Error.Outcome.Should().Be("not_executed");
        audit.Received(1).Write(Arg.Is<GithubieAuditEvent>(entry => entry.Tool == "github_branch_protection_update"
            && entry.Repository == "sha256:AF2BDBE1AA9B6EC1E2ADE1D694F41FC71A831D0268E9891562113D8A62ADD1BF" && entry.Branch == "sha256:0D6E4079E36703EBD37C00722F5891D28B0E2811DC114B129215123ADCCE3605" && entry.Diagnostic == null));
    }

    [Fact]
    public void UnknownMutation_IsNeverMarkedRetryable()
    {
        var result = GithubieToolResultMapper.Map("branch_protection_update", "sample",
            GitHubResult<GitHubBranchProtectionMutation>.Failure(GitHubError.BranchProtectionResultUnknown));
        result.Error!.Outcome.Should().Be("unknown");
        result.Error.Retryable.Should().BeFalse();
        result.Error.SuggestedAction.Should().Be("check_status");
    }

    private static GithubieMcpTools Tools(IGitHubBranchProtectionGateway? branchGateway)
    {
        var repositories = new Dictionary<string, RepositoryOptions>
        {
            ["sample"] = new("owner", "repo", "C:\\fixture", "origin", "develop", "main",
                ["develop"], ["develop", "main"], ["main"], "main", "^v", "merge", true),
        };
        return new(Substitute.For<IGitGateway>(), Substitute.For<IGitHubRepositoryGateway>(),
            Substitute.For<IRepositoryRegistrationService>(), Substitute.For<IRepositoryManagementService>(),
            new RepositoryAllowlist(repositories), new GithubieOptions(45460, "/mcp", repositories) { WindowsOwnerSid = "S-1-5-21-test" }, branchGateway);
    }
}
