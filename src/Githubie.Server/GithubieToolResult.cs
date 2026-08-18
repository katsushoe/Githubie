using Githubie.Application.Git;
using Githubie.Application.GitHub;

namespace Githubie.Server;

/// <summary>
/// MCP Toolが返す共通の構造化結果です。内部エラー詳細・スタックトレース・HTTP応答本文は露出しません。
/// </summary>
public sealed record GithubieToolResult<T>(bool Ok, string Operation, string Repository, T? Data, GithubieToolError? Error)
{
    public static GithubieToolResult<T> Success(string operation, string repository, T data) =>
        new(true, operation, repository, data, null);

    public static GithubieToolResult<T> Failure(string operation, string repository, GithubieToolError error) =>
        new(false, operation, repository, default, error);
}

/// <summary>
/// MCP Toolのエラーを表す固定コード + 人間可読メッセージです。
/// </summary>
public sealed record GithubieToolError(string Code, string Message);

/// <summary>
/// <see cref="GitGatewayError"/> / <see cref="GitHubError"/>をMCP Tool向けの固定エラーコードへ変換します。
/// </summary>
public static class GithubieToolResultMapper
{
    public static GithubieToolResult<T> Map<T>(string operation, string repository, GitGatewayResult<T> result) =>
        result.IsSuccess
            ? GithubieToolResult<T>.Success(operation, repository, result.Value!)
            : GithubieToolResult<T>.Failure(operation, repository, MapGitError(result.Error!.Value));

    public static GithubieToolResult<T> Map<T>(string operation, string repository, GitHubResult<T> result) =>
        result.IsSuccess
            ? GithubieToolResult<T>.Success(operation, repository, result.Value!)
            : GithubieToolResult<T>.Failure(operation, repository, MapGitHubError(result.Error!.Value));

    private static GithubieToolError MapGitError(GitGatewayError error) => error switch
    {
        GitGatewayError.RepositoryNotFound => new("repository_not_found", "Repository is not registered."),
        GitGatewayError.RepositoryNotAllowed => new("repository_not_allowed", "Repository is not in the allowlist."),
        GitGatewayError.LocalRootNotFound => new("local_root_not_found", "Local repository root was not found."),
        GitGatewayError.GitMetadataNotFound => new("git_metadata_not_found", "Local root does not contain a .git directory."),
        GitGatewayError.ReparsePointDetected => new("reparse_point_detected", "Local root path contains a symlink or junction."),
        GitGatewayError.RemoteMismatch => new("remote_mismatch", "Git remote does not match the configured repository."),
        GitGatewayError.GitNotFound => new("git_not_found", "git executable was not found."),
        GitGatewayError.GitFailed => new("git_failed", "Git command failed."),
        GitGatewayError.GitTimedOut => new("timeout", "Git command timed out."),
        GitGatewayError.GitCancelled => new("git_failed", "Git command was cancelled."),
        GitGatewayError.WorkingTreeDirty => new("working_tree_dirty", "Working tree has uncommitted changes."),
        GitGatewayError.BranchNotAllowed => new("branch_not_allowed", "Branch is not allowed for this operation."),
        GitGatewayError.ProtectedBranch => new("protected_branch", "Direct push to a protected branch is not allowed."),
        GitGatewayError.NothingToPush => new("nothing_to_push", "There is nothing to push."),
        GitGatewayError.NonFastForward => new("non_fast_forward", "Fast-forward pull is not possible."),
        _ => new("git_failed", "Git operation failed."),
    };

    private static GithubieToolError MapGitHubError(GitHubError error) => error switch
    {
        GitHubError.RepositoryNotFound => new("repository_not_found", "Repository is not registered."),
        GitHubError.AuthenticationFailed => new("authentication_failed", "GitHub authentication failed."),
        GitHubError.PermissionDenied => new("permission_denied", "GitHub denied the operation."),
        GitHubError.TokenScopeMissing => new("token_scope_missing", "Personal Access Token is missing required permissions."),
        GitHubError.ApiError => new("github_api_error", "GitHub API returned an error."),
        GitHubError.RateLimited => new("rate_limited", "GitHub primary rate limit was exceeded."),
        GitHubError.SecondaryRateLimited => new("secondary_rate_limited", "GitHub secondary rate limit was exceeded."),
        GitHubError.InvalidResponse => new("github_api_error", "GitHub API returned an unexpected response."),
        GitHubError.PullRequestNotFound => new("pull_request_not_found", "Pull request was not found."),
        GitHubError.PullRequestNotOpen => new("pull_request_not_open", "Pull request is not open."),
        GitHubError.PullRequestNotMergeable => new("pull_request_not_mergeable", "Pull request is not mergeable."),
        GitHubError.PullRequestRouteNotAllowed => new("pull_request_route_not_allowed", "Pull request route is not allowed."),
        GitHubError.TagInvalid => new("tag_invalid", "Tag name does not match the allowed pattern."),
        GitHubError.TagAlreadyExists => new("tag_already_exists", "Tag already exists."),
        GitHubError.TagTargetNotAllowed => new("tag_target_not_allowed", "Tag target branch is not allowed."),
        GitHubError.NetworkError => new("network_error", "Network error occurred while calling GitHub."),
        GitHubError.Timeout => new("timeout", "GitHub API call timed out."),
        _ => new("github_api_error", "GitHub operation failed."),
    };
}
