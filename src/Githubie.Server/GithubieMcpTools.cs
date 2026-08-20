using System.ComponentModel;
using System.Reflection;
using Githubie.Application.Git;
using Githubie.Application.GitHub;
using Githubie.Application.Repositories;
using ModelContextProtocol.Server;

namespace Githubie.Server;

/// <summary>
/// Githubieが公開するMCP Toolです。Tool名には`github_`Prefixを付け、変更系操作は<c>Destructive = true</c>を明示します。
/// </summary>
[McpServerToolType]
public sealed class GithubieMcpTools(
    IGitGateway gitGateway,
    IGitHubRepositoryGateway gitHubGateway,
    IRepositoryRegistrationService registrationService,
    IRepositoryManagementService managementService)
{
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
        CancellationToken cancellationToken = default)
    {
        var request = new RepositoryUpdateRequest(
            direct_push_branches, pull_branches, protected_branches,
            tag_target_branch, tag_pattern, require_clean_working_tree);
        var result = await managementService.UpdateAsync(repository, request, cancellationToken);
        return GithubieToolResultMapper.Map("repository_update", repository, result);
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
        return GithubieToolResultMapper.Map("push", repository, result);
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
    [Description("Release Tagを作成します。既定ではmain HEADのみを対象とします。")]
    public async Task<GithubieToolResult<GitHubTagInfo>> CreateTagAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("Tag名(例: v1.0.0)")] string tag,
        [Description("Annotated tag message")] string? message,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.CreateTagAsync(repository, tag, message, cancellationToken);
        return GithubieToolResultMapper.Map("tag_create", repository, result);
    }

    [McpServerTool(Name = "github_release_create", Destructive = true, UseStructuredContent = true)]
    [Description("既存Tagからdraft Releaseを作成し、Repository配下のMSI/ZIP/SHA-256を添付後に公開します。")]
    public async Task<GithubieToolResult<GitHubReleaseInfo>> CreateReleaseAsync(
        [Description("Githubie内部のRepository ID")] string repository,
        [Description("既存Tag名")] string tag,
        [Description("Release名")] string name,
        [Description("Release note")] string? body,
        [Description("Draftのまま保持するか")] bool draft,
        [Description("Pre-releaseとして扱うか")] bool prerelease,
        [Description("Repository local root配下の添付ファイル絶対パス一覧")] IReadOnlyList<string> assets,
        CancellationToken cancellationToken)
    {
        var result = await gitHubGateway.CreateReleaseAsync(
            repository, new GitHubReleaseCreate(tag, name, body, draft, prerelease, assets), cancellationToken);
        return GithubieToolResultMapper.Map("release_create", repository, result);
    }

    [McpServerTool(Name = "get_version", ReadOnly = true, UseStructuredContent = true)]
    [Description("Githubie Serverのバージョンを取得します。")]
    public GithubieToolResult<string> GetVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        return GithubieToolResult<string>.Success("get_version", string.Empty, version);
    }
}
