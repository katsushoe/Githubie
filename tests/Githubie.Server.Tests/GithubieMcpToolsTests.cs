using System.Reflection;
using Githubie.Application.Git;
using Githubie.Application.GitHub;
using Githubie.Application.Repositories;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using Xunit;

namespace Githubie.Server.Tests;

/// <summary>
/// 公開Tool一覧を型リフレクションで固定検証する契約テストです。
/// Tool追加・変更時は本テストの期待値も更新してください。
/// `McpServerToolAttribute.Destructive`はMCP仕様のdestructiveHint既定値(true)をそのまま返すため、
/// 明示指定の有無をリフレクションで判定できません。destructiveHintの実配線は実機のtools/list応答で確認済みです。
/// </summary>
public sealed class GithubieMcpToolsTests
{
    [Fact]
    public void BranchCreate_SchemaRequiresSource()
    {
        var gateway = Substitute.For<IGitHubRepositoryGateway>();
        var instance = CreateTools(gateway);
        var tool = McpServerTool.Create(typeof(GithubieMcpTools).GetMethod(nameof(GithubieMcpTools.CreateBranchAsync))!, instance);

        tool.ProtocolTool.InputSchema.GetProperty("required").EnumerateArray()
            .Select(item => item.GetString()).Should().Contain("source");
    }

    [Theory]
    [InlineData("release/base")]
    [InlineData("1111111111111111111111111111111111111111")]
    public async Task BranchCreate_ForwardsSourceThroughAudit(string source)
    {
        var gateway = Substitute.For<IGitHubRepositoryGateway>();
        var audit = Substitute.For<IGithubieAuditLogger>();
        gateway.CreateBranchAsync("sample", "develop", source, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubBranchInfo>.Success(new("develop", "sha", false)));
        var tools = CreateTools(new AuditedGitHubRepositoryGateway(gateway, audit));

        var result = await tools.CreateBranchAsync("sample", "develop", source, TestContext.Current.CancellationToken);

        result.Ok.Should().BeTrue();
        await gateway.Received(1).CreateBranchAsync("sample", "develop", source, Arg.Any<CancellationToken>());
        audit.Received(1).Write(Arg.Is<GithubieAuditEvent>(e => e.Tool == "github_branch_create" && e.Result == "success" && e.Source == source));
    }

    [Fact]
    public async Task TagCreate_PersistsTheTagInTheSameRegisteredRepositoryBeforeSuccess()
    {
        const string repository = "sample";
        const string tag = "v1.2.3";
        const string sha = "1111111111111111111111111111111111111111";
        var gitGateway = Substitute.For<IGitGateway>();
        var gitHubGateway = Substitute.For<IGitHubRepositoryGateway>();
        gitHubGateway.CreateTagAsync(repository, tag, sha, null, Arg.Any<CancellationToken>()).Returns(GitHubResult<GitHubTagInfo>.Success(new(tag, sha, null, null, null)));
        gitGateway.PersistTagAsync(repository, tag, Arg.Any<CancellationToken>()).Returns(GitGatewayResult<Unit>.Success(Unit.Value));
        var tools = new GithubieMcpTools(gitGateway, gitHubGateway, Substitute.For<IRepositoryRegistrationService>(), Substitute.For<IRepositoryManagementService>(), new RepositoryAllowlist(new Dictionary<string, Githubie.Application.Configuration.RepositoryOptions>()));
        var result = await tools.CreateTagAsync(repository, tag, sha, null, TestContext.Current.CancellationToken);
        result.Ok.Should().BeTrue();
        await gitGateway.Received(1).PersistTagAsync(repository, tag, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TagCreate_WhenLocalPersistenceFails_ReturnsTypedFailure()
    {
        const string repository = "sample";
        const string tag = "v1.2.3";
        const string sha = "1111111111111111111111111111111111111111";
        var gitGateway = Substitute.For<IGitGateway>();
        var gitHubGateway = Substitute.For<IGitHubRepositoryGateway>();
        gitHubGateway.CreateTagAsync(repository, tag, sha, null, Arg.Any<CancellationToken>()).Returns(GitHubResult<GitHubTagInfo>.Success(new(tag, sha, null, null, null)));
        gitGateway.PersistTagAsync(repository, tag, Arg.Any<CancellationToken>()).Returns(GitGatewayResult<Unit>.Failure(GitGatewayError.GitFailed, "tag collision"));
        var tools = new GithubieMcpTools(gitGateway, gitHubGateway, Substitute.For<IRepositoryRegistrationService>(), Substitute.For<IRepositoryManagementService>(), new RepositoryAllowlist(new Dictionary<string, Githubie.Application.Configuration.RepositoryOptions>()));
        var result = await tools.CreateTagAsync(repository, tag, sha, null, TestContext.Current.CancellationToken);
        result.Ok.Should().BeFalse();
        result.Error!.Code.Should().Be("git_failed");
        result.Error.Diagnostic.Should().Be("tag collision");
    }

    [Fact]
    public async Task ReleaseCreate_MoyaiVersionContract_CreatesDraftForVTag()
    {
        var gateway = Substitute.For<IGitHubRepositoryGateway>();
        gateway.CreateReleaseAsync("sample", Arg.Any<GitHubReleaseCreate>(), Arg.Any<CancellationToken>())
            .Returns(call => GitHubResult<GitHubReleaseInfo>.Success(new(
                1, call.ArgAt<GitHubReleaseCreate>(1).Tag, "1.2.3", true, false, "https://example.com", [])));
        var tools = CreateTools(gateway);

        var result = await tools.CreateReleaseAsync(
            "sample", version: "1.2.3", notes: "notes", project: "sample",
            cancellationToken: TestContext.Current.CancellationToken);

        result.Ok.Should().BeTrue();
        await gateway.Received(1).CreateReleaseAsync("sample",
            Arg.Is<GitHubReleaseCreate>(request => request.Tag == "v1.2.3" && request.Draft && request.Body == "notes"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReleasePublish_AfterDraftCreateWithArtifact_FindsDraftAndIsIdempotent()
    {
        var gateway = Substitute.For<IGitHubRepositoryGateway>();
        var asset = new GitHubReleaseAssetInfo("artifact.zip", 231, "https://example.com/artifact.zip", 20);
        var release = new GitHubReleaseInfo(10, "v1.2.3", "1.2.3", true, false, "https://example.com", [asset]);
        gateway.CreateReleaseAsync("sample", Arg.Any<GitHubReleaseCreate>(), Arg.Any<CancellationToken>())
            .Returns(_ => GitHubResult<GitHubReleaseInfo>.Success(release));
        gateway.GetReleaseAsync("sample", "v1.2.3", Arg.Any<CancellationToken>())
            .Returns(_ => release.Draft
                ? GitHubResult<GitHubReleaseInfo>.Failure(GitHubError.ReleaseNotFound)
                : GitHubResult<GitHubReleaseInfo>.Success(release));
        gateway.ListReleasesAsync("sample", Arg.Any<CancellationToken>())
            .Returns(_ => GitHubResult<IReadOnlyList<GitHubReleaseInfo>>.Success([release]));
        gateway.UpdateReleaseAsync("sample", 10, Arg.Any<GitHubReleaseUpdate>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                release = release with { Draft = call.ArgAt<GitHubReleaseUpdate>(2).Draft ?? release.Draft };
                return GitHubResult<GitHubReleaseInfo>.Success(release);
            });
        var tools = CreateTools(gateway);

        var created = await tools.CreateReleaseAsync(
            "sample", version: "1.2.3", artifact_path: "artifact.zip", project: "sample",
            cancellationToken: TestContext.Current.CancellationToken);
        var published = await tools.PublishReleaseAsync(
            "sample", "1.2.3", project: "sample", cancellationToken: TestContext.Current.CancellationToken);
        var repeated = await tools.PublishReleaseAsync(
            "sample", "1.2.3", project: "sample", cancellationToken: TestContext.Current.CancellationToken);

        created.Ok.Should().BeTrue();
        published.Ok.Should().BeTrue();
        repeated.Ok.Should().BeTrue();
        release.Draft.Should().BeFalse();
        release.Assets.Should().ContainSingle().Which.Should().Be(asset);
        await gateway.Received(2).UpdateReleaseAsync("sample", 10,
            Arg.Is<GitHubReleaseUpdate>(request => request.Draft == false), Arg.Any<CancellationToken>());
        await gateway.DidNotReceive().UploadReleaseAssetsAsync(
            Arg.Any<string>(), Arg.Any<GitHubReleaseAssetUpload>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReleaseWithdraw_DeletesReleaseButNotTag()
    {
        var gateway = Substitute.For<IGitHubRepositoryGateway>();
        gateway.GetReleaseAsync("sample", "v1.2.3", Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubReleaseInfo>.Success(new(7, "v1.2.3", "1.2.3", false, false, "https://example.com", [])));
        gateway.DeleteReleaseAsync("sample", 7, Arg.Any<CancellationToken>()).Returns(GitHubResult<bool>.Success(true));
        var tools = CreateTools(gateway);

        var result = await tools.WithdrawReleaseAsync(
            "sample", "1.2.3", "sample", TestContext.Current.CancellationToken);

        result.Ok.Should().BeTrue();
        await gateway.Received(1).DeleteReleaseAsync("sample", 7, Arg.Any<CancellationToken>());
        await gateway.DidNotReceive().DeleteTagAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReleaseDraftDelete_DeletesDraftById()
    {
        var gateway = Substitute.For<IGitHubRepositoryGateway>();
        gateway.DeleteDraftReleaseAsync("sample", 42, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<bool>.Success(true));
        var tools = CreateTools(gateway);

        var result = await tools.DeleteDraftReleaseAsync(
            "sample", 42, TestContext.Current.CancellationToken);

        result.Ok.Should().BeTrue();
        await gateway.Received(1).DeleteDraftReleaseAsync("sample", 42, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(GitHubError.BranchSourceInvalid, "branch_source_invalid")]
    [InlineData(GitHubError.BranchSourceNotFound, "branch_source_not_found")]
    [InlineData(GitHubError.TagSourceInvalid, "tag_source_invalid")]
    [InlineData(GitHubError.TagSourceNotFound, "tag_source_not_found")]
    public void BranchCreate_MapsSourceErrors(GitHubError error, string code) =>
        GithubieToolResultMapper.MapError(error).Code.Should().Be(code);

    private static GithubieMcpTools CreateTools(IGitHubRepositoryGateway gateway) => new(
        Substitute.For<IGitGateway>(), gateway, Substitute.For<IRepositoryRegistrationService>(),
        Substitute.For<IRepositoryManagementService>(), new RepositoryAllowlist(new Dictionary<string, Githubie.Application.Configuration.RepositoryOptions>()));

    private static readonly string[] ExpectedToolNames =
    [
        "list_projects",
        "github_repository_status",
        "github_repository_diff",
        "github_repository_commit",
        "github_repository_description_get",
        "github_repository_description_update",
        "github_workflow_dispatch",
        "github_workflow_run_get",
        "github_workflow_run_list",
        "github_repository_register",
        "github_repository_unregister",
            "github_repository_update",
            "github_repository_rename",
        "github_fetch",
        "github_pull",
        "github_push",
        "github_history_rewrite",
        "github_branch_list",
        "github_branch_get",
        "github_provider_capabilities",
        "github_branch_create",
        "github_branch_delete",
        "github_pr_list",
        "github_pr_get",
        "github_issue_list",
        "github_issue_get",
        "github_pr_diff",
        "github_pr_create",
        "github_pr_merge",
        "github_pr_close",
        "github_pr_reopen",
        "github_pr_comment_list",
        "github_pr_comment_create",
        "github_pr_review_approve",
        "github_pr_review_request_changes",
        "github_tag_list",
        "github_tag_get",
        "github_tag_create",
        "github_tag_delete",
        "github_tag_push",
        "github_release_list",
        "github_release_get",
        "github_release_update",
        "github_release_asset_upload",
        "github_release_create",
        "github_release_publish",
        "github_release_draft_delete",
        "github_release_withdraw",
        "get_version",
    ];

    private static readonly string[] ExpectedReadOnlyTools =
    [
        "list_projects",
        "github_repository_status",
        "github_repository_diff",
        "github_repository_description_get",
        "github_workflow_run_get",
        "github_workflow_run_list",
        "github_branch_list",
        "github_branch_get",
        "github_provider_capabilities",
        "github_pr_list",
        "github_pr_get",
        "github_issue_list",
        "github_issue_get",
        "github_pr_diff",
        "github_pr_comment_list",
        "github_tag_list",
        "github_tag_get",
        "github_release_list",
        "github_release_get",
        "get_version",
    ];

    private static IReadOnlyList<(MethodInfo Method, McpServerToolAttribute Attribute)> GetToolMethods() =>
        typeof(GithubieMcpTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => (Method: m, Attribute: m.GetCustomAttribute<McpServerToolAttribute>()))
            .Where(x => x.Attribute is not null)
            .Select(x => (x.Method, x.Attribute!))
            .ToArray();

    [Fact]
    public void ToolNames_MatchExpectedSet()
    {
        var names = GetToolMethods().Select(t => t.Attribute.Name).ToArray();

        names.Should().BeEquivalentTo(ExpectedToolNames);
    }

    [Fact]
    public void ReadOnlyTools_MatchExpectedSet()
    {
        var readOnly = GetToolMethods().Where(t => t.Attribute.ReadOnly).Select(t => t.Attribute.Name).ToArray();

        readOnly.Should().BeEquivalentTo(ExpectedReadOnlyTools);
    }

    [Fact]
    public void AllToolNames_HaveGithubPrefixOrAreDocumentedExceptions()
    {
        foreach (var (_, attribute) in GetToolMethods())
        {
            (attribute.Name!.StartsWith("github_", StringComparison.Ordinal)
                || attribute.Name is "get_version" or "list_projects")
                .Should().BeTrue($"tool '{attribute.Name}' should use the github_ prefix");
        }
    }

    [Fact]
    public void AllParameterNames_AreSnakeCase()
    {
        // 実機検証で発見した回帰防止: MCP Tool入力スキーマはメソッドのC#パラメータ名をそのまま使うため、
        // camelCase(例: pullRequestNumber)のまま公開するとstructured outputのsnake_caseと不整合になる。
        foreach (var (method, _) in GetToolMethods())
        {
            foreach (var parameter in method.GetParameters())
            {
                if (parameter.ParameterType == typeof(CancellationToken))
                {
                    continue;
                }

                parameter.Name.Should().NotMatchRegex("[A-Z]", $"parameter '{parameter.Name}' on '{method.Name}' should be snake_case");
            }
        }
    }
}
