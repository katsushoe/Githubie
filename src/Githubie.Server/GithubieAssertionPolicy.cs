using Moyai.ProviderAuthentication;

namespace Githubie.Server;

/// <summary>GithubieのRepository ToolをProvider Assertion Scopeへ固定対応付けします。</summary>
public static class GithubieAssertionPolicy
{
    public const string ProviderId = "githubie";
    public const string ProtocolVersion = "1";

    private static readonly IReadOnlyDictionary<string, string[]> ToolScopes =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["github_repository_status"] = ["repository.read"],
            ["github_repository_diff"] = ["repository.read"],
            ["github_repository_commit"] = ["repository.commit"],
            ["github_repository_description_get"] = ["repository.read"],
            ["github_repository_description_update"] = ["provider.githubie.repository.description.write"],
            ["github_workflow_dispatch"] = ["provider.githubie.workflow.dispatch"],
            ["github_workflow_run_get"] = ["repository.read"],
            ["github_workflow_run_list"] = ["repository.read"],
            ["github_fetch"] = ["repository.read"],
            ["github_pull"] = ["repository.pull"],
            ["github_push"] = ["repository.push"],
            ["github_history_rewrite"] = ["provider.githubie.history.rewrite"],
            ["github_branch_list"] = ["repository.read"],
            ["github_provider_capabilities"] = ["repository.read"],
            ["github_branch_get"] = ["repository.read"],
            ["github_branch_create"] = ["repository.branch.write"],
            ["github_branch_delete"] = ["repository.branch.write"],
            ["github_pr_list"] = ["repository.read"],
            ["github_pr_get"] = ["repository.read"],
            ["github_issue_list"] = ["repository.read"],
            ["github_issue_get"] = ["repository.read"],
            ["github_pr_diff"] = ["repository.read"],
            ["github_pr_create"] = ["provider.githubie.pull_request.write"],
            ["github_pr_merge"] = ["provider.githubie.pull_request.write"],
            ["github_pr_close"] = ["provider.githubie.pull_request.write"],
            ["github_pr_reopen"] = ["provider.githubie.pull_request.write"],
            ["github_pr_comment_list"] = ["repository.read"],
            ["github_pr_comment_create"] = ["provider.githubie.pull_request.comment.write"],
            ["github_pr_review_approve"] = ["provider.githubie.pull_request.review.write"],
            ["github_pr_review_request_changes"] = ["provider.githubie.pull_request.review.write"],
            ["github_tag_push"] = ["repository.tag.write"],
            ["github_tag_list"] = ["repository.read"],
            ["github_tag_get"] = ["repository.read"],
            ["github_tag_create"] = ["repository.tag.write"],
            ["github_tag_delete"] = ["repository.tag.write"],
            ["github_release_list"] = ["repository.read"],
            ["github_release_get"] = ["repository.read"],
            ["github_release_update"] = ["release.publish"],
            ["github_release_asset_upload"] = ["artifact.upload"],
            ["github_release_create"] = ["release.publish", "artifact.upload"],
            ["github_release_publish"] = ["release.publish"],
            ["github_release_draft_delete"] = ["release.publish"],
            ["github_release_withdraw"] = ["release.publish"],
        };

    /// <summary>共通Validatorへ渡すCapabilityを作成します。</summary>
    public static AssertionCapability CreateCapability() =>
        new(ProviderId, ProviderId, ProtocolVersion, "ES256", true, ToolScopes);

    /// <summary>Repository Toolに必要な固定Scopeを取得します。</summary>
    public static bool TryGetScopes(string tool, out string[] scopes) =>
        ToolScopes.TryGetValue(tool, out scopes!);
}
