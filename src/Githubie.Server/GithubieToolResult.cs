using Githubie.Application.Git;
using Githubie.Application.GitHub;
using Githubie.Application.Repositories;

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

    public static GithubieToolResult<RepositoryRegistrationInfo> Map(
        string operation,
        string repository,
        RepositoryRegistrationResult result) =>
        result.IsSuccess
            ? GithubieToolResult<RepositoryRegistrationInfo>.Success(operation, repository, result.Value!)
            : GithubieToolResult<RepositoryRegistrationInfo>.Failure(
                operation, repository, MapRegistrationError(result.Error!.Value));

    public static GithubieToolResult<RepositoryMutationInfo> Map(
        string operation,
        string repository,
        RepositoryMutationResult result) =>
        result.IsSuccess
            ? GithubieToolResult<RepositoryMutationInfo>.Success(operation, repository, result.Value!)
            : GithubieToolResult<RepositoryMutationInfo>.Failure(
                operation, repository, MapMutationError(result.Error!.Value));

    private static GithubieToolError MapMutationError(RepositoryMutationError error) => error switch
    {
        RepositoryMutationError.InvalidRepositoryId => new("invalid_repository_id", "Repository ID is invalid."),
        RepositoryMutationError.RepositoryNotRegistered => new("repository_not_registered", "Repository is not registered."),
        RepositoryMutationError.InvalidPolicy => new("invalid_policy", "Repository branch policy is invalid."),
        RepositoryMutationError.ApprovalDenied => new("approval_denied", "Repository update was denied."),
        RepositoryMutationError.ApprovalTimedOut => new("approval_timed_out", "Repository update approval timed out."),
        RepositoryMutationError.ApprovalUnavailable => new("approval_unavailable", "The approval prompt could not be displayed."),
        RepositoryMutationError.PersistenceFailed => new("persistence_failed", "Repository configuration could not be saved."),
        RepositoryMutationError.DuplicateRepositoryId => new("duplicate_repository_id", "The new Repository ID is already registered."),
        RepositoryMutationError.TokenNotFound => new("token_not_found", "The source Repository has no stored token."),
        RepositoryMutationError.CredentialMigrationFailed => new("credential_migration_failed", "The encrypted token could not be migrated."),
        _ => new("internal", "Repository mutation failed."),
    };

    private static GithubieToolError MapRegistrationError(RepositoryRegistrationError error) => error switch
    {
        RepositoryRegistrationError.InvalidRepositoryId => new("invalid_repository_id", "Repository ID is invalid."),
        RepositoryRegistrationError.DuplicateRepositoryId => new("duplicate_repository_id", "Repository ID is already registered."),
        RepositoryRegistrationError.InvalidLocalRoot => new("invalid_local_root", "Local repository root is invalid or missing."),
        RepositoryRegistrationError.GitMetadataNotFound => new("git_metadata_not_found", "Local root does not contain Git metadata."),
        RepositoryRegistrationError.ReparsePointDetected => new("reparse_point_detected", "Local root path contains a symlink or junction."),
        RepositoryRegistrationError.InvalidRemote => new("invalid_remote", "Git remote is missing or invalid."),
        RepositoryRegistrationError.NonGitHubRemote => new("non_github_remote", "Git remote does not point to github.com."),
        RepositoryRegistrationError.GitFailed => new("git_failed", "Git remote validation failed."),
        RepositoryRegistrationError.ApprovalDenied => new("approval_denied", "Repository registration was denied."),
        RepositoryRegistrationError.ApprovalTimedOut => new("approval_timed_out", "Repository registration approval timed out."),
        RepositoryRegistrationError.ApprovalUnavailable => new("approval_unavailable", "The approval prompt could not be displayed."),
        RepositoryRegistrationError.PersistenceFailed => new("persistence_failed", "Repository configuration could not be saved."),
        _ => new("internal", "Repository registration failed."),
    };

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
        GitGatewayError.AuthenticationFailed => new("authentication_failed", "Git authentication failed."),
        GitGatewayError.WorkingTreeDirty => new("working_tree_dirty", "Working tree has uncommitted changes."),
        GitGatewayError.BranchNotAllowed => new("branch_not_allowed", "Branch is not allowed for this operation."),
        GitGatewayError.ProtectedBranch => new("protected_branch", "Direct push to a protected branch is not allowed."),
        GitGatewayError.InvalidRef => new("invalid_ref", "A history rewrite ref or SHA is invalid."),
        GitGatewayError.DuplicateRef => new("duplicate_ref", "A history rewrite ref was specified more than once."),
        GitGatewayError.LeaseConflict => new("lease_conflict", "A remote ref changed or does not match the expected SHA."),
        GitGatewayError.AtomicNotSupported => new("atomic_not_supported", "The remote does not support atomic push."),
        GitGatewayError.BranchProtectionDenied => new("branch_protection_denied", "A branch protection rule or repository ruleset rejected the history rewrite."),
        GitGatewayError.TokenPermissionDenied => new("token_permission_denied", "The configured token does not have permission to update the repository."),
        GitGatewayError.WorkflowPermissionDenied => new("workflow_permission_denied", "The token cannot update workflow files; grant the required workflow permission."),
        GitGatewayError.ApprovalDenied => new("approval_denied", "The history rewrite was denied."),
        GitGatewayError.ApprovalTimedOut => new("approval_timed_out", "The history rewrite approval timed out."),
        GitGatewayError.ApprovalUnavailable => new("approval_unavailable", "The approval prompt could not be displayed."),
        GitGatewayError.PermissionDenied => new("permission_denied", "GitHub denied the Git operation."),
        GitGatewayError.NothingToPush => new("nothing_to_push", "There is nothing to push."),
        GitGatewayError.NonFastForward => new("non_fast_forward", "The remote contains changes that prevent a fast-forward operation."),
        _ => new("git_failed", "Git operation failed."),
    };

    private static GithubieToolError MapGitHubError(GitHubError error) => error switch
    {
        GitHubError.RepositoryNotFound => new("repository_not_found", "Repository is not registered."),
        GitHubError.BranchNotFound => new("branch_not_found", "Branch was not found."),
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
        GitHubError.PullRequestStateNotAllowed => new("pull_request_state_not_allowed", "A merged pull request cannot be closed or reopened."),
        GitHubError.PullRequestCommentInvalid => new("pull_request_comment_invalid", "Pull request comment body is invalid."),
        GitHubError.PullRequestReviewInvalid => new("pull_request_review_invalid", "Pull request review input or state is invalid."),
        GitHubError.TagNotFound => new("tag_not_found", "Tag was not found."),
        GitHubError.TagInvalid => new("tag_invalid", "Tag name does not match the allowed pattern."),
        GitHubError.TagAlreadyExists => new("tag_already_exists", "Tag already exists."),
        GitHubError.TagTargetNotAllowed => new("tag_target_not_allowed", "Tag target branch is not allowed."),
        GitHubError.ReleaseAlreadyExists => new("release_already_exists", "A release already exists for the tag."),
        GitHubError.ReleaseAssetInvalid => new("release_asset_invalid", "Release assets must be unique MSI, ZIP, or SHA-256 files under the repository root."),
        GitHubError.ReleaseAssetNotFound => new("release_asset_not_found", "A release asset file was not found."),
        GitHubError.ReleaseUploadFailed => new("release_upload_failed", "A release asset could not be uploaded; the release remains a draft."),
        GitHubError.NetworkError => new("network_error", "Network error occurred while calling GitHub."),
        GitHubError.Timeout => new("timeout", "GitHub API call timed out."),
        _ => new("github_api_error", "GitHub operation failed."),
    };
}
