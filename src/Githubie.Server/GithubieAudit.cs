using System.Diagnostics;
using Githubie.Application.Git;
using Githubie.Application.GitHub;
using Githubie.Application.Repositories;
using Microsoft.Extensions.Logging;

namespace Githubie.Server;

/// <summary>
/// 監査ログの1件を表します。Personal Access Token・Authorization Header・生エラーメッセージは含めません。
/// </summary>
public sealed record GithubieAuditEvent(
    string Client,
    string Tool,
    string Repository,
    string? Branch,
    int? PullRequestNumber,
    string? Tag,
    string Result,
    long DurationMs,
    string? ErrorCode,
    string? NewRepository = null);

public interface IGithubieAuditLogger
{
    void Write(GithubieAuditEvent auditEvent);
}

public sealed class GithubieAuditLogger(ILogger<GithubieAuditLogger> logger) : IGithubieAuditLogger
{
    private readonly ILogger<GithubieAuditLogger> _logger = logger;

    public void Write(GithubieAuditEvent auditEvent) => _logger.LogInformation(
        "client={Client} tool={Tool} repository={Repository} branch={Branch} pull_request_number={PullRequestNumber} tag={Tag} result={Result} duration_ms={DurationMs} error_code={ErrorCode}",
        auditEvent.Client, auditEvent.Tool, auditEvent.Repository, auditEvent.Branch, auditEvent.PullRequestNumber, auditEvent.Tag, auditEvent.Result, auditEvent.DurationMs, auditEvent.ErrorCode);
}

/// <summary>
/// <see cref="IGitGateway"/>呼び出しを計測し監査ログへ記録するデコレーターです。
/// </summary>
public sealed class AuditedGitGateway(IGitGateway inner, IGithubieAuditLogger audit) : IGitGateway
{
    public async Task<GitGatewayResult<GitRepositoryStatus>> GetStatusAsync(string repository, CancellationToken cancellationToken) =>
        await RunAsync("github_repository_status", repository, null, () => inner.GetStatusAsync(repository, cancellationToken));

    public async Task<GitGatewayResult<Unit>> FetchAsync(string repository, CancellationToken cancellationToken) =>
        await RunAsync("github_fetch", repository, null, () => inner.FetchAsync(repository, cancellationToken));

    public async Task<GitGatewayResult<Unit>> PullAsync(string repository, string branch, CancellationToken cancellationToken) =>
        await RunAsync("github_pull", repository, branch, () => inner.PullAsync(repository, branch, cancellationToken));

    public async Task<GitGatewayResult<Unit>> PushAsync(string repository, CancellationToken cancellationToken) =>
        await RunAsync("github_push", repository, null, () => inner.PushAsync(repository, cancellationToken));

    public async Task<GitGatewayResult<GitHistoryRewriteResult>> RewriteHistoryAsync(
        string repository, IReadOnlyList<GitHistoryRewriteRef> refs, bool dryRun, CancellationToken cancellationToken) =>
        await RunAsync("github_history_rewrite", repository, null, () => inner.RewriteHistoryAsync(repository, refs, dryRun, cancellationToken));

    private async Task<GitGatewayResult<T>> RunAsync<T>(string tool, string repository, string? branch, Func<Task<GitGatewayResult<T>>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await action();
        stopwatch.Stop();

        audit.Write(new GithubieAuditEvent(
            Client: "mcp", Tool: tool, Repository: repository, Branch: branch, PullRequestNumber: null, Tag: null,
            Result: result.IsSuccess ? "success" : "failure", DurationMs: stopwatch.ElapsedMilliseconds,
            ErrorCode: result.IsSuccess ? null : result.Error!.Value.ToString()));

        return result;
    }
}

/// <summary>
/// <see cref="IRepositoryRegistrationService"/>呼び出しを計測し、秘密値を含まない結果コードだけを監査ログへ記録します。
/// </summary>
public sealed class AuditedRepositoryRegistrationService(
    IRepositoryRegistrationService inner,
    IGithubieAuditLogger audit) : IRepositoryRegistrationService
{
    public async Task<RepositoryRegistrationResult> RegisterAsync(
        RepositoryRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await inner.RegisterAsync(request, cancellationToken);
        stopwatch.Stop();

        audit.Write(new GithubieAuditEvent(
            Client: "mcp", Tool: "github_repository_register", Repository: request.Repository,
            Branch: null, PullRequestNumber: null, Tag: null,
            Result: result.IsSuccess ? "success" : "failure", DurationMs: stopwatch.ElapsedMilliseconds,
            ErrorCode: result.IsSuccess ? null : result.Error!.Value.ToString()));

        return result;
    }
}

/// <summary>Repository設定変更・登録解除を監査ログへ記録します。</summary>
public sealed class AuditedRepositoryManagementService(
    IRepositoryManagementService inner,
    IGithubieAuditLogger audit) : IRepositoryManagementService
{
    public Task<RepositoryMutationResult> UpdateAsync(
        string repositoryId, RepositoryUpdateRequest request, CancellationToken cancellationToken) =>
        RunAsync("github_repository_update", repositoryId,
            () => inner.UpdateAsync(repositoryId, request, cancellationToken));

    public Task<RepositoryMutationResult> UnregisterAsync(
        string repositoryId, CancellationToken cancellationToken) =>
        RunAsync("github_repository_unregister", repositoryId,
            () => inner.UnregisterAsync(repositoryId, cancellationToken));

    public Task<RepositoryMutationResult> RenameAsync(
        string oldRepositoryId, string newRepositoryId, CancellationToken cancellationToken) =>
        RunAsync("github_repository_rename", oldRepositoryId,
            () => inner.RenameAsync(oldRepositoryId, newRepositoryId, cancellationToken), newRepositoryId);

    private async Task<RepositoryMutationResult> RunAsync(
        string tool, string repository, Func<Task<RepositoryMutationResult>> action, string? newRepository = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await action();
        stopwatch.Stop();
        audit.Write(new GithubieAuditEvent(
            "mcp", tool, repository, null, null, null,
            result.IsSuccess ? "success" : "failure", stopwatch.ElapsedMilliseconds,
            result.IsSuccess ? null : result.Error!.Value.ToString(), newRepository));
        return result;
    }
}

/// <summary>
/// <see cref="IGitHubRepositoryGateway"/>呼び出しを計測し監査ログへ記録するデコレーターです。
/// </summary>
public sealed class AuditedGitHubRepositoryGateway(IGitHubRepositoryGateway inner, IGithubieAuditLogger audit) : IGitHubRepositoryGateway
{
    public Task<GitHubResult<IReadOnlyList<GitHubBranchInfo>>> ListBranchesAsync(string repository, CancellationToken cancellationToken) =>
        RunAsync("github_branch_list", repository, null, null, () => inner.ListBranchesAsync(repository, cancellationToken));

    public Task<GitHubResult<GitHubBranchInfo>> GetBranchAsync(string repository, string branch, CancellationToken cancellationToken) =>
        RunAsync("github_branch_get", repository, branch, null, () => inner.GetBranchAsync(repository, branch, cancellationToken));

    public Task<GitHubResult<IReadOnlyList<GitHubPullRequestInfo>>> ListPullRequestsAsync(
        string repository, GitHubPullRequestState? state, string? source, string? destination, CancellationToken cancellationToken) =>
        RunAsync("github_pr_list", repository, null, null, () => inner.ListPullRequestsAsync(repository, state, source, destination, cancellationToken));

    public Task<GitHubResult<GitHubPullRequestInfo>> GetPullRequestAsync(string repository, int number, CancellationToken cancellationToken) =>
        RunAsync("github_pr_get", repository, null, number, () => inner.GetPullRequestAsync(repository, number, cancellationToken));

    public Task<GitHubResult<GitHubPullRequestDiff>> GetPullRequestDiffAsync(string repository, int number, CancellationToken cancellationToken) =>
        RunAsync("github_pr_diff", repository, null, number, () => inner.GetPullRequestDiffAsync(repository, number, cancellationToken));

    public Task<GitHubResult<GitHubPullRequestInfo>> CreatePullRequestAsync(string repository, GitHubPullRequestCreate request, CancellationToken cancellationToken) =>
        RunAsync("github_pr_create", repository, null, null, () => inner.CreatePullRequestAsync(repository, request, cancellationToken));

    public Task<GitHubResult<GitHubPullRequestInfo>> MergePullRequestAsync(string repository, GitHubPullRequestMerge request, CancellationToken cancellationToken) =>
        RunAsync("github_pr_merge", repository, null, request.Number, () => inner.MergePullRequestAsync(repository, request, cancellationToken));

    public Task<GitHubResult<GitHubPullRequestInfo>> ClosePullRequestAsync(string repository, int number, CancellationToken cancellationToken) =>
        RunAsync("github_pr_close", repository, null, number, () => inner.ClosePullRequestAsync(repository, number, cancellationToken));

    public Task<GitHubResult<GitHubPullRequestInfo>> ReopenPullRequestAsync(string repository, int number, CancellationToken cancellationToken) =>
        RunAsync("github_pr_reopen", repository, null, number, () => inner.ReopenPullRequestAsync(repository, number, cancellationToken));

    public Task<GitHubResult<IReadOnlyList<GitHubPullRequestComment>>> ListPullRequestCommentsAsync(
        string repository, int number, CancellationToken cancellationToken) =>
        RunAsync("github_pr_comment_list", repository, null, number, () => inner.ListPullRequestCommentsAsync(repository, number, cancellationToken));

    public Task<GitHubResult<GitHubPullRequestComment>> CreatePullRequestCommentAsync(
        string repository, int number, string body, CancellationToken cancellationToken) =>
        RunAsync("github_pr_comment_create", repository, null, number, () => inner.CreatePullRequestCommentAsync(repository, number, body, cancellationToken));

    public Task<GitHubResult<GitHubPullRequestReview>> ApprovePullRequestAsync(
        string repository, int number, string? body, CancellationToken cancellationToken) =>
        RunAsync("github_pr_review_approve", repository, null, number, () => inner.ApprovePullRequestAsync(repository, number, body, cancellationToken));

    public Task<GitHubResult<GitHubPullRequestReview>> RequestPullRequestChangesAsync(
        string repository, int number, string body, CancellationToken cancellationToken) =>
        RunAsync("github_pr_review_request_changes", repository, null, number, () => inner.RequestPullRequestChangesAsync(repository, number, body, cancellationToken));

    public Task<GitHubResult<IReadOnlyList<GitHubTagInfo>>> ListTagsAsync(string repository, CancellationToken cancellationToken) =>
        RunAsync("github_tag_list", repository, null, null, () => inner.ListTagsAsync(repository, cancellationToken));

    public Task<GitHubResult<GitHubTagInfo>> GetTagAsync(string repository, string tag, CancellationToken cancellationToken) =>
        RunAsync("github_tag_get", repository, null, null, () => inner.GetTagAsync(repository, tag, cancellationToken), tag);

    public Task<GitHubResult<GitHubTagInfo>> CreateTagAsync(string repository, string tag, string? message, CancellationToken cancellationToken) =>
        RunAsync("github_tag_create", repository, null, null, () => inner.CreateTagAsync(repository, tag, message, cancellationToken), tag);

    public Task<GitHubResult<bool>> DeleteTagAsync(string repository, string tag, CancellationToken cancellationToken) =>
        RunAsync("github_tag_delete", repository, null, null, () => inner.DeleteTagAsync(repository, tag, cancellationToken), tag);

    public Task<GitHubResult<IReadOnlyList<GitHubReleaseInfo>>> ListReleasesAsync(string repository, CancellationToken cancellationToken) =>
        RunAsync("github_release_list", repository, null, null, () => inner.ListReleasesAsync(repository, cancellationToken));

    public Task<GitHubResult<GitHubReleaseInfo>> GetReleaseAsync(string repository, string tag, CancellationToken cancellationToken) =>
        RunAsync("github_release_get", repository, null, null, () => inner.GetReleaseAsync(repository, tag, cancellationToken), tag);

    public Task<GitHubResult<GitHubReleaseInfo>> UpdateReleaseAsync(
        string repository, long releaseId, GitHubReleaseUpdate request, CancellationToken cancellationToken) =>
        RunAsync("github_release_update", repository, null, null, () => inner.UpdateReleaseAsync(repository, releaseId, request, cancellationToken));

    public Task<GitHubResult<GitHubReleaseInfo>> UploadReleaseAssetsAsync(
        string repository, GitHubReleaseAssetUpload request, CancellationToken cancellationToken) =>
        RunAsync("github_release_asset_upload", repository, null, null, () => inner.UploadReleaseAssetsAsync(repository, request, cancellationToken));

    public Task<GitHubResult<GitHubReleaseInfo>> CreateReleaseAsync(
        string repository, GitHubReleaseCreate request, CancellationToken cancellationToken) =>
        RunAsync("github_release_create", repository, null, null, () => inner.CreateReleaseAsync(repository, request, cancellationToken), request.Tag);

    private async Task<GitHubResult<T>> RunAsync<T>(
        string tool, string repository, string? branch, int? pullRequestNumber, Func<Task<GitHubResult<T>>> action, string? tag = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await action();
        stopwatch.Stop();

        audit.Write(new GithubieAuditEvent(
            Client: "mcp", Tool: tool, Repository: repository, Branch: branch, PullRequestNumber: pullRequestNumber, Tag: tag,
            Result: result.IsSuccess ? "success" : "failure", DurationMs: stopwatch.ElapsedMilliseconds,
            ErrorCode: result.IsSuccess ? null : result.Error!.Value.ToString()));

        return result;
    }
}
