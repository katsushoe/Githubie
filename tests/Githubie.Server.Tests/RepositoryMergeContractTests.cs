using FluentAssertions;
using Githubie.Application.Git;
using Githubie.Application.GitHub;
using Githubie.Application.Repositories;
using NSubstitute;
using Xunit;

namespace Githubie.Server.Tests;

/// <summary>github_repository_merge のMCP受け渡し、監査記録、Provider認可scopeを固定します。</summary>
public sealed class RepositoryMergeContractTests
{
    private static readonly string Head = new('a', 40);
    private static readonly string Source = new('b', 40);
    private static readonly string Merged = new('c', 40);

    [Fact]
    public async Task McpTool_PassesExplicitArgumentsUnchangedToTheGateway()
    {
        var gateway = Substitute.For<IGitGateway>();
        gateway.MergeAsync("sample", "develop", Head, GitMergeSourceKind.RemoteBranch, "origin/main", Source,
                GitMergeFastForwardMode.Never, "merge", Arg.Any<IReadOnlyList<GitMergeResolution>?>(), Arg.Any<CancellationToken>())
            .Returns(GitGatewayResult<GitRepositoryMergeInfo>.Success(new("develop", Merged, [Head, Source], false, Head, Source, ["README.md"])));
        var tools = new GithubieMcpTools(gateway, Substitute.For<IGitHubRepositoryGateway>(), Substitute.For<IRepositoryRegistrationService>(),
            Substitute.For<IRepositoryManagementService>(), new RepositoryAllowlist(new Dictionary<string, Githubie.Application.Configuration.RepositoryOptions>()));
        IReadOnlyList<GitMergeResolution> resolutions = [new("README.md", GitMergeResolution.Source)];

        var result = await tools.MergeRepositoryAsync("sample", "develop", Head, GitMergeSourceKind.RemoteBranch, "origin/main", Source,
            GitMergeFastForwardMode.Never, "merge", resolutions, TestContext.Current.CancellationToken);

        result.Ok.Should().BeTrue();
        result.Operation.Should().Be("repository_merge");
        result.Data!.CommitSha.Should().Be(Merged);
        await gateway.Received(1).MergeAsync("sample", "develop", Head, GitMergeSourceKind.RemoteBranch, "origin/main", Source,
            GitMergeFastForwardMode.Never, "merge", resolutions, Arg.Any<CancellationToken>());
        await gateway.DidNotReceive().PushAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AuditedGateway_RecordsMergeWithTargetBranchAndResult(bool success)
    {
        var inner = Substitute.For<IGitGateway>();
        inner.MergeAsync("sample", "develop", Head, GitMergeSourceKind.Branch, "main", Source, GitMergeFastForwardMode.Allow, null, null, Arg.Any<CancellationToken>())
            .Returns(success
                ? GitGatewayResult<GitRepositoryMergeInfo>.Success(new("develop", Merged, [Head, Source], false, Head, Source, []))
                : GitGatewayResult<GitRepositoryMergeInfo>.Failure(GitGatewayError.MergeConflict) with { Candidates = ["README.md"] });
        var audit = Substitute.For<IGithubieAuditLogger>();

        var result = await new AuditedGitGateway(inner, audit).MergeAsync("sample", "develop", Head, GitMergeSourceKind.Branch, "main", Source,
            GitMergeFastForwardMode.Allow, null, null, TestContext.Current.CancellationToken);

        audit.Received(1).Write(Arg.Is<GithubieAuditEvent>(item =>
            item.Tool == "github_repository_merge" && item.Repository == "sample" && item.Branch == "develop"
            && item.Result == (success ? "success" : "failure")
            && item.ErrorCode == (success ? null : nameof(GitGatewayError.MergeConflict))));
        if (!success) result.Candidates.Should().Equal("README.md");
    }

    [Fact]
    public void ProviderAuthorization_UsesTheExistingCommitScope()
    {
        GithubieAssertionPolicy.OperationNames.Should().Contain("github_repository_merge");
    }
}
