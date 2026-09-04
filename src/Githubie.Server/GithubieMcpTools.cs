using System.ComponentModel;
using System.Reflection;
using Githubie.Application.Git;
using Githubie.Application.GitHub;
using Githubie.Application.Repositories;
using ModelContextProtocol.Server;

namespace Githubie.Server;

/// <summary>
/// Githubieが公開するMCP Toolです。Tool名には原則`github_`Prefixを付け、変更系操作は<c>Destructive = true</c>を明示します。
/// </summary>
[McpServerToolType]
public sealed class GithubieMcpTools(
    IGitGateway gitGateway,
    IGitHubRepositoryGateway gitHubGateway,
    IRepositoryRegistrationService registrationService,
    IRepositoryManagementService managementService,
    RepositoryAllowlist repositoryAllowlist)
{
    [McpServerTool(Name = "list_projects", ReadOnly = true, UseStructuredContent = true)]
    [Description("Githubieに登録済みのRepository ID一覧を取得します。")]
    public GithubieToolResult<IReadOnlyList<string>> ListProjects()
    {
        var repositories = repositoryAllowlist.RepositoryIds
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return GithubieToolResult<IReadOnlyList<string>>.Success(
            "list_projects", string.Empty, repositories);
    }

    [McpServerTool(Name = "github_repository_register", Destructive = true, UseStructuredContent = true)]
    [Description("ローカルGit remoteからGitHub接続先を導出し、対話承認後にRepositoryを登録します。")]
    public async Task<GithubieToolResult<RepositoryRegistrationInfo>> RegisterRepositoryAsync(
        [Description("Githubie内部の新規Repository ID")] string repository,
        [Description("既存ローカルGit Repositoryの絶対Path")] string local_root,
        [Description("検証・使用するGit remote名。省略時はorigin")] string? remote,
        [Description("開発Branch名。省略時はdevelop")] string? develop_branch,
        [Description("主要Branch名。省略時はmain")] string? main_branch,
        CancellationToken cancellationToken)
    {
        var result = await registrationService.RegisterAsync(
            new RepositoryRegistrationRequest(repository, local_root, remote, develop_branch, main_branch),
            cancellationToken);
        return GithubieToolResultMapper.Map("repository_register", repository, result);
    }

    [McpServerTool(Name = "github_repository_unregister", Destructive = true, UseStructuredContent = true)]
    [Description("登録済みRepositoryをGithubieの設定とAllowlistから登録解除します。GitHub/ローカルRepositoryは削除しません。")]
    public async Task<GithubieToolResult<RepositoryMutationInfo>> UnregisterRepositoryAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        CancellationToken cancellationToken = default)
    {
        var result = await managementService.UnregisterAsync(repository, cancellationToken);
        return GithubieToolResultMapper.Map("repository_unregister", repository, result);
    }

    [McpServerTool(Name = "github_repository_update", Destructive = true, UseStructuredContent = true)]
    [Description("登録済みRepositoryのBranch Policyを対話承認後に更新します。識別情報とLocal Rootは変更しません。")]
    public async Task<GithubieToolResult<RepositoryMutationInfo>> UpdateRepositoryAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("直接Pushを許可するBranch")] IReadOnlyList<string> direct_push_branches,
        [Description("Pullを許可するBranch")] IReadOnlyList<string> pull_branches,
        [Description("直接Pushから保護するBranch")] IReadOnlyList<string> protected_branches,
        [Description("Release Tag対象Branch")] string tag_target_branch,
        [Description("許可するTag名の正規表現")] string tag_pattern,
        [Description("Push時にcleanな作業Treeを要求するか")] bool require_clean_working_tree = true,
        [Description("起動を許可するworkflow別Policy。省略時は既存設定を維持")] IReadOnlyDictionary<string, Application.Configuration.WorkflowPolicyOptions>? workflows = null,
        CancellationToken cancellationToken = default)
    {
        var request = new RepositoryUpdateRequest(
            direct_push_branches, pull_branches, protected_branches,
            tag_target_branch, tag_pattern, require_clean_working_tree, workflows);
        var result = await managementService.UpdateAsync(repository, request, cancellationToken);
        return GithubieToolResultMapper.Map("repository_update", repository, result);
    }

    [McpServerTool(Name = "github_repository_rename", Destructive = true, UseStructuredContent = true)]
    [Description("Repository設定と暗号化Tokenを旧IDから新IDへ一括移行します。")]
    public async Task<GithubieToolResult<RepositoryMutationInfo>> RenameRepositoryAsync(
        [Description("現在のRepository ID")] string old_repository,
        [Description("新しいRepository ID")] string new_repository,
        CancellationToken cancellationToken = default)
    {
        var result = await managementService.RenameAsync(old_repository, new_repository, cancellationToken);
        return GithubieToolResultMapper.Map("repository_rename", old_repository, result);
    }

    [McpServerTool(Name = "github_repository_status", ReadOnly = true, UseStructuredContent = true)]
    [Description("指定リポジトリのGit状態(local/remote head, ahead/behind, working tree clean)を取得します。")]
    public async Task<GithubieToolResult<GitRepositoryStatus>> GetRepositoryStatusAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        CancellationToken cancellationToken)
    {
        var result = await gitGateway.GetStatusAsync(repository, cancellationToken);
        return GithubieToolResultMapper.Map("repository_status", repository, result);
    }

    [McpServerTool(Name = "github_repository_diff", ReadOnly = true, UseStructuredContent = true)]
    [Description("登録Repositoryのworking tree差分を取得します。")]
    public async Task<GithubieToolResult<GitRepositoryDiff>> GetRepositoryDiffAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        CancellationToken cancellationToken)
    {
        var result = await gitGateway.GetDiffAsync(repository, cancellationToken);
        return GithubieToolResultMapper.Map("repository_diff", repository, result);
    }

    [McpServerTool(Name = "github_repository_commit", Destructive = true, UseStructuredContent = true)]
    [Description("Policyで許可されたbranchにLocal Commitを作成します。")]
    public async Task<GithubieToolResult<GitRepositoryCommit>> CommitRepositoryAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("Commit message")] string message,
        CancellationToken cancellationToken)
    {
        var result = await gitGateway.CommitAsync(repository, message, cancellationToken);
        return GithubieToolResultMapper.Map("repository_commit", repository, result);
    }

    [McpServerTool(Name = "github_repository_description_get", ReadOnly = true, UseStructuredContent = true)]
    [Description("登録済みリポジトリのDescriptionを取得します。")]
    public async Task<GithubieToolResult<GitHubRepositoryInfo>> GetRepositoryDescriptionAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.GetRepositoryAsync(repository, cancellationToken);
        return GithubieToolResultMapper.Map("repository_description_get", repository, result);
    }

    [McpServerTool(Name = "github_repository_description_update", Destructive = true, UseStructuredContent = true)]
    [Description("登録済みリポジトリのDescriptionを更新します。空文字列で削除します。")]
    public async Task<GithubieToolResult<GitHubRepositoryInfo>> UpdateRepositoryDescriptionAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("新しいDescription。空文字列で削除")] string description,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.UpdateRepositoryDescriptionAsync(repository, description, cancellationToken);
        return GithubieToolResultMapper.Map("repository_description_update", repository, result);
    }

    [McpServerTool(Name = "github_workflow_dispatch", Destructive = true, UseStructuredContent = true)]
    [Description("許可済みGitHub Actions workflowを許可ref・検証済みinputsで起動し、対応runを一意に特定します。")]
    public async Task<GithubieToolResult<GitHubWorkflowDispatchInfo>> DispatchWorkflowAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("設定で許可されたworkflowファイル名またはID")] string workflow,
        [Description("設定で許可されたGit ref")] string @ref,
        [Description("workflowごとの許可スキーマに適合するinputs")] IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.DispatchWorkflowAsync(
            repository, new GitHubWorkflowDispatchRequest(workflow, @ref, inputs), cancellationToken);
        return GithubieToolResultMapper.Map("workflow_dispatch", repository, result);
    }

    [McpServerTool(Name = "github_workflow_run_get", ReadOnly = true, UseStructuredContent = true)]
    [Description("GitHub Actions workflow runをrun IDで取得します。log本文は返しません。")]
    public async Task<GithubieToolResult<GitHubWorkflowRunInfo>> GetWorkflowRunAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("Workflow run ID")] long run_id,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.GetWorkflowRunAsync(repository, run_id, cancellationToken);
        return GithubieToolResultMapper.Map("workflow_run_get", repository, result);
    }

    [McpServerTool(Name = "github_workflow_run_list", ReadOnly = true, UseStructuredContent = true)]
    [Description("GitHub Actions workflow run一覧を最大100件取得します。log本文は返しません。")]
    public async Task<GithubieToolResult<IReadOnlyList<GitHubWorkflowRunInfo>>> ListWorkflowRunsAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("許可済みworkflow。省略時は全workflow")] string? workflow,
        [Description("許可branchフィルタ")] string? branch,
        [Description("Eventフィルタ")] string? event_name,
        [Description("Statusフィルタ")] string? status,
        [Description("取得上限。1から100")] int limit,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.ListWorkflowRunsAsync(
            repository, workflow, branch, event_name, status, limit, cancellationToken);
        return GithubieToolResultMapper.Map("workflow_run_list", repository, result);
    }

    [McpServerTool(Name = "github_fetch", UseStructuredContent = true)]
    [Description("設定済みRemoteからgit fetch相当を行います。")]
    public async Task<GithubieToolResult<Unit>> FetchAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        CancellationToken cancellationToken)
    {
        var result = await gitGateway.FetchAsync(repository, cancellationToken);
        return GithubieToolResultMapper.Map("fetch", repository, result);
    }

    [McpServerTool(Name = "github_pull", UseStructuredContent = true)]
    [Description("git pull --ff-only相当を行います。Fast-forward不能な場合はエラーを返します。")]
    public async Task<GithubieToolResult<Unit>> PullAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("Pull対象branch")] string branch,
        CancellationToken cancellationToken)
    {
        var result = await gitGateway.PullAsync(repository, branch, cancellationToken);
        return GithubieToolResultMapper.Map("pull", repository, result);
    }

    [McpServerTool(Name = "github_push", Destructive = true, UseStructuredContent = true)]
    [Description("ローカルCommitをGitHubへPushします。mainなどProtected Branchへの直接Pushは拒否します。")]
    public async Task<GithubieToolResult<Unit>> PushAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        CancellationToken cancellationToken)
    {
        var result = await gitGateway.PushAsync(repository, cancellationToken);
        var mapped = GithubieToolResultMapper.Map("push", repository, result);
        if (mapped.Error?.Code is "repository_not_found" or "repository_not_allowed")
        {
            var candidates = repositoryAllowlist.RepositoryIds
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return mapped with { Error = mapped.Error with { Candidates = candidates } };
        }

        return mapped;
    }

    [McpServerTool(Name = "github_history_rewrite", Destructive = true, UseStructuredContent = true)]
    [Description("複数のbranch/tag refをatomicかつforce-with-leaseで履歴訂正します。実更新は対話承認を要求します。")]
    public async Task<GithubieToolResult<GitHistoryRewriteResult>> RewriteHistoryAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("対象ref、新local SHA、期待remote SHAの一覧")] IReadOnlyList<GitHistoryRewriteRef> refs,
        [Description("更新せず検証計画だけを返すか")] bool dry_run,
        CancellationToken cancellationToken)
    {
        var result = await gitGateway.RewriteHistoryAsync(repository, refs, dry_run, cancellationToken);
        return GithubieToolResultMapper.Map("history_rewrite", repository, result);
    }

    [McpServerTool(Name = "github_branch_list", ReadOnly = true, UseStructuredContent = true)]
    [Description("Remote Branch一覧を取得します。")]
    public async Task<GithubieToolResult<IReadOnlyList<GitHubBranchInfo>>> ListBranchesAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.ListBranchesAsync(repository, cancellationToken);
        return GithubieToolResultMapper.Map("branch_list", repository, result);
    }

    [McpServerTool(Name = "github_provider_capabilities", ReadOnly = true, UseStructuredContent = true)]
    [Description("Repository Contractで利用できるGithubie操作を返します。")]
    public async Task<GithubieToolResult<GitHubProviderCapabilities>> GetProviderCapabilitiesAsync(
        string repository, CancellationToken cancellationToken)
    {
        var repositoryResult = await gitHubGateway.GetRepositoryAsync(repository, cancellationToken);
        if (!repositoryResult.IsSuccess)
            return GithubieToolResult<GitHubProviderCapabilities>.Failure(
                "provider_capabilities", repository, GithubieToolResultMapper.MapError(repositoryResult.Error!.Value));
        return GithubieToolResult<GitHubProviderCapabilities>.Success(
            "provider_capabilities", repository, new(true, true, true, true, true, true, true, true, true));
    }

    [McpServerTool(Name = "github_branch_get", ReadOnly = true, UseStructuredContent = true)]
    [Description("指定Branchのhead commit sha等を取得します。")]
    public async Task<GithubieToolResult<GitHubBranchInfo>> GetBranchAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("Branch名")] string branch,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.GetBranchAsync(repository, branch, cancellationToken);
        return GithubieToolResultMapper.Map("branch_get", repository, result);
    }

    [McpServerTool(Name = "github_branch_create", Destructive = true, UseStructuredContent = true)]
    [Description("許可されたBranchを明示した作成元から作成します。sourceは必須で、暗黙の補完は行いません。")]
    public async Task<GithubieToolResult<GitHubBranchInfo>> CreateBranchAsync(
        string repository, string branch,
        [Description("作成元のBranch名または完全な40桁コミットSHA。必須。省略・空白はエラー。")] string source,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.CreateBranchAsync(repository, branch, source, cancellationToken);
        return GithubieToolResultMapper.Map("branch_create", repository, result);
    }

    [McpServerTool(Name = "github_branch_delete", Destructive = true, UseStructuredContent = true)]
    [Description("許可された非保護Branchを削除します。")]
    public async Task<GithubieToolResult<bool>> DeleteBranchAsync(
        string repository, string branch, CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.DeleteBranchAsync(repository, branch, cancellationToken);
        return GithubieToolResultMapper.Map("branch_delete", repository, result);
    }

    [McpServerTool(Name = "github_pr_list", ReadOnly = true, UseStructuredContent = true)]
    [Description("Pull Request一覧を取得します。")]
    public async Task<GithubieToolResult<IReadOnlyList<GitHubPullRequestInfo>>> ListPullRequestsAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("状態フィルタ(open/closed/merged)")] GitHubPullRequestState? state,
        [Description("Sourceブランチフィルタ")] string? source,
        [Description("Destinationブランチフィルタ")] string? destination,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.ListPullRequestsAsync(repository, state, source, destination, cancellationToken);
        return GithubieToolResultMapper.Map("pr_list", repository, result);
    }

    [McpServerTool(Name = "github_pr_get", ReadOnly = true, UseStructuredContent = true)]
    [Description("Pull Requestの詳細を取得します。")]
    public async Task<GithubieToolResult<GitHubPullRequestInfo>> GetPullRequestAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("Pull Request番号")] int pull_request_number,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.GetPullRequestAsync(repository, pull_request_number, cancellationToken);
        return GithubieToolResultMapper.Map("pr_get", repository, result);
    }

    [McpServerTool(Name = "github_issue_list", ReadOnly = true, UseStructuredContent = true)]
    [Description("GitHub Issue一覧を取得します。Pull Requestは含みません。")]
    public async Task<GithubieToolResult<IReadOnlyList<GitHubIssueInfo>>> ListIssuesAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("状態フィルタ(open/closed)。省略時は全状態")]
        GitHubIssueState? state,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.ListIssuesAsync(repository, state, cancellationToken);
        return GithubieToolResultMapper.Map("issue_list", repository, result);
    }

    [McpServerTool(Name = "github_issue_get", ReadOnly = true, UseStructuredContent = true)]
    [Description("GitHub Issueの詳細を取得します。Pull Request番号はIssueとして返しません。")]
    public async Task<GithubieToolResult<GitHubIssueInfo>> GetIssueAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("Issue番号")] int issue_number,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.GetIssueAsync(repository, issue_number, cancellationToken);
        return GithubieToolResultMapper.Map("issue_get", repository, result);
    }

    [McpServerTool(Name = "github_pr_diff", ReadOnly = true, UseStructuredContent = true)]
    [Description("Pull Requestの差分(diff・変更統計)を取得します。")]
    public async Task<GithubieToolResult<GitHubPullRequestDiff>> GetPullRequestDiffAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("Pull Request番号")] int pull_request_number,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.GetPullRequestDiffAsync(repository, pull_request_number, cancellationToken);
        return GithubieToolResultMapper.Map("pr_diff", repository, result);
    }

    [McpServerTool(Name = "github_pr_create", Destructive = true, UseStructuredContent = true)]
    [Description("develop→mainのPull Requestを作成します。Source/Destinationは設定から固定されます。")]
    public async Task<GithubieToolResult<GitHubPullRequestInfo>> CreatePullRequestAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("PRタイトル")] string title,
        [Description("PR説明")] string? description,
        [Description("Draft PRとして作成するか")] bool draft,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.CreatePullRequestAsync(repository, new GitHubPullRequestCreate(title, description, draft), cancellationToken);
        return GithubieToolResultMapper.Map("pr_create", repository, result);
    }

    [McpServerTool(Name = "github_pr_merge", Destructive = true, UseStructuredContent = true)]
    [Description("Pull Requestをmergeします。State==open、Source/Destinationが許可経路であることを検証します。")]
    public async Task<GithubieToolResult<GitHubPullRequestInfo>> MergePullRequestAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("Pull Request番号")] int pull_request_number,
        [Description("merge/squash/rebase。省略時はリポジトリ設定の既定値")] GitHubMergeMethod? merge_strategy,
        [Description("Merge commit message")] string? message,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.MergePullRequestAsync(
            repository, new GitHubPullRequestMerge(pull_request_number, merge_strategy, message), cancellationToken);
        return GithubieToolResultMapper.Map("pr_merge", repository, result);
    }

    [McpServerTool(Name = "github_pr_close", Destructive = true, UseStructuredContent = true)]
    [Description("未mergeのPull Requestを閉じます。GitHub上から削除はされません。")]
    public async Task<GithubieToolResult<GitHubPullRequestInfo>> ClosePullRequestAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("Pull Request番号")] int pull_request_number,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.ClosePullRequestAsync(repository, pull_request_number, cancellationToken);
        return GithubieToolResultMapper.Map("pr_close", repository, result);
    }

    [McpServerTool(Name = "github_pr_reopen", Destructive = true, UseStructuredContent = true)]
    [Description("閉じた未mergeのPull Requestを再度開きます。")]
    public async Task<GithubieToolResult<GitHubPullRequestInfo>> ReopenPullRequestAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("Pull Request番号")] int pull_request_number,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.ReopenPullRequestAsync(repository, pull_request_number, cancellationToken);
        return GithubieToolResultMapper.Map("pr_reopen", repository, result);
    }

    [McpServerTool(Name = "github_pr_comment_list", ReadOnly = true, UseStructuredContent = true)]
    [Description("Pull Request全体への会話Comment一覧を取得します。")]
    public async Task<GithubieToolResult<IReadOnlyList<GitHubPullRequestComment>>> ListPullRequestCommentsAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("Pull Request番号")] int pull_request_number,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.ListPullRequestCommentsAsync(repository, pull_request_number, cancellationToken);
        return GithubieToolResultMapper.Map("pr_comment_list", repository, result);
    }

    [McpServerTool(Name = "github_pr_comment_create", Destructive = true, UseStructuredContent = true)]
    [Description("Pull Request全体へ会話Commentを追加します。")]
    public async Task<GithubieToolResult<GitHubPullRequestComment>> CreatePullRequestCommentAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("Pull Request番号")] int pull_request_number,
        [Description("Comment本文")] string body,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.CreatePullRequestCommentAsync(repository, pull_request_number, body, cancellationToken);
        return GithubieToolResultMapper.Map("pr_comment_create", repository, result);
    }

    [McpServerTool(Name = "github_pr_review_approve", Destructive = true, UseStructuredContent = true)]
    [Description("開いているPull Requestを承認します。")]
    public async Task<GithubieToolResult<GitHubPullRequestReview>> ApprovePullRequestAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("Pull Request番号")] int pull_request_number,
        [Description("任意のReview本文")] string? body,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.ApprovePullRequestAsync(repository, pull_request_number, body, cancellationToken);
        return GithubieToolResultMapper.Map("pr_review_approve", repository, result);
    }

    [McpServerTool(Name = "github_pr_review_request_changes", Destructive = true, UseStructuredContent = true)]
    [Description("開いているPull Requestへ変更を要求します。Review本文は必須です。")]
    public async Task<GithubieToolResult<GitHubPullRequestReview>> RequestPullRequestChangesAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("Pull Request番号")] int pull_request_number,
        [Description("変更要求のReview本文")] string body,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.RequestPullRequestChangesAsync(repository, pull_request_number, body, cancellationToken);
        return GithubieToolResultMapper.Map("pr_review_request_changes", repository, result);
    }

    [McpServerTool(Name = "github_tag_push", Destructive = true, UseStructuredContent = true)]
    [Description("既存の許可されたLocal TagをRemoteへ明示的にPushします。")]
    public async Task<GithubieToolResult<Unit>> PushTagAsync(
        string repository, string tag, CancellationToken cancellationToken)
    {
        var result = await gitGateway.PushTagAsync(repository, tag, cancellationToken);
        return GithubieToolResultMapper.Map("tag_push", repository, result);
    }

    [McpServerTool(Name = "github_tag_list", ReadOnly = true, UseStructuredContent = true)]
    [Description("Repository Tag一覧を取得します。")]
    public async Task<GithubieToolResult<IReadOnlyList<GitHubTagInfo>>> ListTagsAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.ListTagsAsync(repository, cancellationToken);
        return GithubieToolResultMapper.Map("tag_list", repository, result);
    }

    [McpServerTool(Name = "github_tag_get", ReadOnly = true, UseStructuredContent = true)]
    [Description("Tag詳細を取得します。")]
    public async Task<GithubieToolResult<GitHubTagInfo>> GetTagAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("Tag名")] string tag,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.GetTagAsync(repository, tag, cancellationToken);
        return GithubieToolResultMapper.Map("tag_get", repository, result);
    }

    [McpServerTool(Name = "github_tag_create", Destructive = true, UseStructuredContent = true)]
    [Description("明示したbranchまたは完全な40桁commit SHAを作成元としてRelease Tagを作成します。")]
    public async Task<GithubieToolResult<GitHubTagInfo>> CreateTagAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("Tag名(例: v1.0.0)")] string tag,
        [Description("作成元branch名または完全な40桁commit SHA（必須）")] string source,
        [Description("Annotated tag message")] string? message,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.CreateTagAsync(repository, tag, source, message, cancellationToken);
        if (!result.IsSuccess) return GithubieToolResultMapper.Map("tag_create", repository, result);

        var persisted = await gitGateway.PersistTagAsync(repository, tag, cancellationToken);
        if (!persisted.IsSuccess)
        {
            var error = GithubieToolResultMapper.Map("tag_create", repository, persisted).Error!;
            return GithubieToolResult<GitHubTagInfo>.Failure("tag_create", repository, error);
        }

        return GithubieToolResultMapper.Map("tag_create", repository, result);
    }

    [McpServerTool(Name = "github_tag_delete", Destructive = true, UseStructuredContent = true)]
    [Description("Tagを削除します。")]
    public async Task<GithubieToolResult<bool>> DeleteTagAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("Tag名")] string tag,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.DeleteTagAsync(repository, tag, cancellationToken);
        return GithubieToolResultMapper.Map("tag_delete", repository, result);
    }

    [McpServerTool(Name = "github_release_list", ReadOnly = true, UseStructuredContent = true)]
    [Description("Release一覧を取得します。")]
    public async Task<GithubieToolResult<IReadOnlyList<GitHubReleaseInfo>>> ListReleasesAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.ListReleasesAsync(repository, cancellationToken);
        return GithubieToolResultMapper.Map("release_list", repository, result);
    }

    [McpServerTool(Name = "github_release_get", ReadOnly = true, UseStructuredContent = true)]
    [Description("TagからRelease詳細を取得します。")]
    public async Task<GithubieToolResult<GitHubReleaseInfo>> GetReleaseAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("Tag名（version指定時は省略可）")] string? tag = null,
        [Description("Release版（v接頭辞なし）")] string? version = null,
        [Description("Moyai互換のProject ID。repositoryと一致する場合のみ許可")] string? project = null,
        CancellationToken cancellationToken = default)
    {
        var input = ResolveLifecycleInput(repository, project, tag, version);
        if (input is null) return InvalidRelease(repository, "release_get");
        var result = await gitHubGateway.GetReleaseAsync(repository, input, cancellationToken);
        return GithubieToolResultMapper.Map("release_get", repository, result);
    }

    [McpServerTool(Name = "github_release_update", Destructive = true, UseStructuredContent = true)]
    [Description("Release名、本文、draft、prereleaseを更新します。")]
    public async Task<GithubieToolResult<GitHubReleaseInfo>> UpdateReleaseAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("Release ID")] long release_id,
        [Description("Release名。変更しない場合はnull")] string? name,
        [Description("Release本文。変更しない場合はnull")] string? body,
        [Description("Draft。変更しない場合はnull")] bool? draft,
        [Description("Pre-release。変更しない場合はnull")] bool? prerelease,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.UpdateReleaseAsync(
            repository, release_id, new GitHubReleaseUpdate(name, body, draft, prerelease), cancellationToken);
        return GithubieToolResultMapper.Map("release_update", repository, result);
    }

    [McpServerTool(Name = "github_release_asset_upload", Destructive = true, UseStructuredContent = true)]
    [Description("既存Releaseへ成果物を追加し、明示指定時だけ同名成果物を置換します。")]
    public async Task<GithubieToolResult<GitHubReleaseInfo>> UploadReleaseAssetsAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("Release ID")] long release_id,
        [Description("Repository local root配下の成果物絶対パス一覧")] IReadOnlyList<string> assets,
        [Description("同名成果物を置換するか")] bool replace_existing,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.UploadReleaseAssetsAsync(
            repository, new GitHubReleaseAssetUpload(release_id, assets, replace_existing), cancellationToken);
        return GithubieToolResultMapper.Map("release_asset_upload", repository, result);
    }

    [McpServerTool(Name = "github_release_create", Destructive = true, Idempotent = true, UseStructuredContent = true)]
    [Description("既存TagからReleaseを作成します。Moyaiのversion指定ではdraftとして作成します。")]
    public async Task<GithubieToolResult<GitHubReleaseInfo>> CreateReleaseAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("既存Tag名（version指定時は省略可）")] string? tag = null,
        [Description("Release名")] string? name = null,
        [Description("Release note")] string? body = null,
        [Description("Draftのまま保持するか")] bool? draft = null,
        [Description("Pre-releaseとして扱うか")] bool prerelease = false,
        [Description("Repository local root配下の添付ファイル絶対パス一覧")] IReadOnlyList<string>? assets = null,
        [Description("Release版（v接頭辞なし）")] string? version = null,
        [Description("単一の成果物パス")] string? artifact_path = null,
        [Description("Release notes")] string? notes = null,
        [Description("Moyai互換のProject ID。repositoryと一致する場合のみ許可")] string? project = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedTag = ResolveLifecycleInput(repository, project, tag, version);
        if (resolvedTag is null) return InvalidRelease(repository, "release_create");
        var resolvedAssets = assets?.ToList() ?? [];
        if (!string.IsNullOrWhiteSpace(artifact_path)) resolvedAssets.Add(artifact_path);
        var result = await gitHubGateway.CreateReleaseAsync(
            repository, new GitHubReleaseCreate(resolvedTag, name ?? version ?? resolvedTag, notes ?? body,
                draft ?? version is not null, prerelease, resolvedAssets), cancellationToken);
        return GithubieToolResultMapper.Map("release_create", repository, result);
    }

    [McpServerTool(Name = "github_release_publish", Destructive = true, Idempotent = true, UseStructuredContent = true)]
    [Description("versionに対応するdraft Releaseを公開し、任意の成果物を添付します。")]
    public async Task<GithubieToolResult<GitHubReleaseInfo>> PublishReleaseAsync(
        string repository, string version, string? artifact_path = null, string? notes = null,
        string? project = null, CancellationToken cancellationToken = default)
    {
        var tag = ResolveLifecycleInput(repository, project, null, version);
        if (tag is null) return InvalidRelease(repository, "release_publish");
        var current = await gitHubGateway.GetReleaseAsync(repository, tag, cancellationToken);
        if (!current.IsSuccess && current.Error == GitHubError.ReleaseNotFound)
        {
            var releases = await gitHubGateway.ListReleasesAsync(repository, cancellationToken);
            if (!releases.IsSuccess)
                return GithubieToolResultMapper.Map<GitHubReleaseInfo>("release_publish", repository,
                    GitHubResult<GitHubReleaseInfo>.Failure(releases.Error!.Value));
            var matchingReleases = releases.Value!.Where(release =>
                string.Equals(release.Tag, tag, StringComparison.Ordinal)).Take(2).ToArray();
            if (matchingReleases.Length > 1)
                current = GitHubResult<GitHubReleaseInfo>.Failure(GitHubError.ReleaseAlreadyExists);
            else if (matchingReleases.Length == 1)
                current = GitHubResult<GitHubReleaseInfo>.Success(matchingReleases[0]);
        }
        if (!current.IsSuccess) return GithubieToolResultMapper.Map("release_publish", repository, current);
        var release = current;
        if (!string.IsNullOrWhiteSpace(artifact_path))
            release = await gitHubGateway.UploadReleaseAssetsAsync(repository,
                new GitHubReleaseAssetUpload(current.Value!.Id, [artifact_path], true), cancellationToken);
        if (!release.IsSuccess) return GithubieToolResultMapper.Map("release_publish", repository, release);
        var result = await gitHubGateway.UpdateReleaseAsync(repository, current.Value!.Id,
            new GitHubReleaseUpdate(null, notes, false, null), cancellationToken);
        return GithubieToolResultMapper.Map("release_publish", repository, result);
    }

    [McpServerTool(Name = "github_release_draft_delete", Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Release IDで指定したdraft Releaseだけを削除します。公開済みReleaseとTagは削除しません。")]
    public async Task<GithubieToolResult<bool>> DeleteDraftReleaseAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("削除するdraft Release ID")] long release_id,
        CancellationToken cancellationToken = default)
    {
        var result = await gitHubGateway.DeleteDraftReleaseAsync(repository, release_id, cancellationToken);
        return GithubieToolResultMapper.Map("release_draft_delete", repository, result);
    }

    [McpServerTool(Name = "github_release_withdraw", Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("versionに対応するReleaseを削除します。Tagは保持します。")]
    public async Task<GithubieToolResult<bool>> WithdrawReleaseAsync(
        string repository, string version, string? project = null, CancellationToken cancellationToken = default)
    {
        var tag = ResolveLifecycleInput(repository, project, null, version);
        if (tag is null) return GithubieToolResult<bool>.Failure("release_withdraw", repository,
            new GithubieToolError("invalid_release", "The release input is invalid."));
        var current = await gitHubGateway.GetReleaseAsync(repository, tag, cancellationToken);
        if (!current.IsSuccess)
            return GithubieToolResultMapper.Map<bool>("release_withdraw", repository,
                GitHubResult<bool>.Failure(current.Error!.Value));
        var result = await gitHubGateway.DeleteReleaseAsync(repository, current.Value!.Id, cancellationToken);
        return GithubieToolResultMapper.Map("release_withdraw", repository, result);
    }

    private static string? ResolveLifecycleInput(string repository, string? project, string? tag, string? version)
    {
        if (!string.IsNullOrWhiteSpace(project) && !string.Equals(project, repository, StringComparison.Ordinal)) return null;
        if (!string.IsNullOrWhiteSpace(tag) && !string.IsNullOrWhiteSpace(version))
            return string.Equals(tag, $"v{version}", StringComparison.Ordinal) ? tag : null;
        if (!string.IsNullOrWhiteSpace(tag)) return tag;
        if (string.IsNullOrWhiteSpace(version) || version.StartsWith('v') || version.Length > 128 ||
            version.Any(char.IsWhiteSpace) || version.Any(char.IsControl)) return null;
        return $"v{version}";
    }

    private static GithubieToolResult<GitHubReleaseInfo> InvalidRelease(string repository, string operation) =>
        GithubieToolResult<GitHubReleaseInfo>.Failure(operation, repository,
            new GithubieToolError("invalid_release", "The release input is invalid."));

    [McpServerTool(Name = "get_version", ReadOnly = true, UseStructuredContent = true)]
    [Description("Githubie Serverのバージョンを取得します。")]
    public GithubieToolResult<string> GetVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        return GithubieToolResult<string>.Success("get_version", string.Empty, version);
    }
}
