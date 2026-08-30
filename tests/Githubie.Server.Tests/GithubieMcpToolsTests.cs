using System.Reflection;
using FluentAssertions;
using ModelContextProtocol.Server;
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
    private static readonly string[] ExpectedToolNames =
    [
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
        "get_version",
    ];

    private static readonly string[] ExpectedReadOnlyTools =
    [
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
    public void AllToolNames_HaveGithubPrefixOrAreGetVersion()
    {
        foreach (var (_, attribute) in GetToolMethods())
        {
            (attribute.Name!.StartsWith("github_", StringComparison.Ordinal) || attribute.Name == "get_version")
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
