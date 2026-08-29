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
public sealed record GithubieToolError(string Code, string Message, bool Retryable = false, string? Recommendation = null)
{
    public string Summary => Message;

    public string SuggestedAction => Recommendation ?? "Use the correlation ID to inspect the matching audit log entry.";

    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");

    public string? Status { get; init; }

    public int? RetryAfterSeconds { get; init; }

    public string? Diagnostic { get; init; }

    public int? ExitCode { get; init; }
}

/// <summary>
/// <see cref="GitGatewayError"/> / <see cref="GitHubError"/>をMCP Tool向けの固定エラーコードへ変換します。
/// </summary>
public static class GithubieToolResultMapper
{
    public static GithubieToolError MapError(GitHubError error) => MapGitHubError(error);

    public static GithubieToolResult<T> Map<T>(string operation, string repository, GitGatewayResult<T> result) =>
        result.IsSuccess
            ? GithubieToolResult<T>.Success(operation, repository, result.Value!)
            : GithubieToolResult<T>.Failure(
                operation,
                repository,
                MapGitError(result.Error!.Value) with
                {
                    CorrelationId = result.CorrelationId ?? Guid.NewGuid().ToString("N"),
                    Diagnostic = result.Diagnostic,
                    ExitCode = result.ExitCode,
                });

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
        RepositoryRegistrationError.RemoteHttpsRequired => new("remote_https_required", "Git remote must use an HTTPS GitHub URL."),
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
        GitGatewayError.RemoteHttpsRequired => new("remote_https_required", "Git remote must use an HTTPS GitHub URL."),
        GitGatewayError.GitNotFound => new("git_not_found", "git executable was not found."),
        GitGatewayError.GitFailed => new("git_failed", "Git command failed."),
        GitGatewayError.GitTimedOut => new("timeout", "Git command timed out."),
        GitGatewayError.GitCancelled => new("git_failed", "Git command was cancelled."),
        GitGatewayError.NetworkError => new("network_error", "Git could not reach the remote service.", true, "Check DNS, proxy, TLS, and network connectivity, then retry."),
        GitGatewayError.RemoteUnavailable => new("remote_unavailable", "The configured remote or remote ref is unavailable.", false, "Verify the configured repository, remote, and ref before retrying."),
        GitGatewayError.AuthenticationFailed => new("authentication_failed", "Git authentication failed.", false, "Refresh or replace the configured credential, then retry."),
        GitGatewayError.WorkingTreeDirty => new("working_tree_dirty", "Working tree has uncommitted changes."),
        GitGatewayError.BranchNotAllowed => new("branch_not_allowed", "Branch is not allowed for this operation."),
        GitGatewayError.ProtectedBranch => new("protected_branch", "Direct push to a protected branch is not allowed."),
        GitGatewayError.InvalidRef => new("invalid_ref", "The supplied Git ref is invalid."),
        GitGatewayError.DuplicateRef => new("duplicate_ref", "A history rewrite ref was specified more than once."),
        GitGatewayError.LeaseConflict => new("lease_conflict", "A remote ref changed or does not match the expected SHA."),
        GitGatewayError.AtomicNotSupported => new("atomic_not_supported", "The remote does not support atomic push."),
        GitGatewayError.BranchProtectionDenied => new("branch_protection_denied", "A branch protection rule or repository ruleset rejected the history rewrite."),
        GitGatewayError.TokenPermissionDenied => new("token_permission_denied", "The configured token does not have permission to update the repository."),
        GitGatewayError.WorkflowPermissionDenied => new("workflow_permission_denied", "The token cannot update workflow files; grant the required workflow permission."),
        GitGatewayError.ApprovalDenied => new("approval_denied", "The history rewrite was denied."),
        GitGatewayError.ApprovalTimedOut => new("approval_timed_out", "The history rewrite approval timed out."),
        GitGatewayError.ApprovalUnavailable => new("approval_unavailable", "The approval prompt could not be displayed."),
        GitGatewayError.PermissionDenied => new("permission_denied", "GitHub denied the Git operation.", false, "Verify repository and token permissions before retrying."),
        GitGatewayError.NothingToPush => new("nothing_to_push", "There is nothing to push."),
        GitGatewayError.NonFastForward => new("non_fast_forward", "The remote contains changes that prevent a fast-forward operation.", false, "Fetch and reconcile the remote changes before retrying."),
        _ => new("git_failed", "Git operation failed for an unclassified safe diagnostic category."),
    };

    private static GithubieToolError MapGitHubError(GitHubError error) => error switch
    {
        GitHubError.RepositoryNotFound => new("repository_not_found", "Repository is not registered."),
        GitHubError.RepositoryDescriptionInvalid => new("repository_description_invalid", "Repository description must be at most 350 characters."),
        GitHubError.WorkflowNotAllowed => new("workflow_not_allowed", "Workflow is not allowed by repository policy."),
        GitHubError.WorkflowRefNotAllowed => new("workflow_ref_not_allowed", "Workflow ref is not allowed by repository policy."),
        GitHubError.WorkflowInputInvalid => new("workflow_input_invalid", "Workflow inputs do not match the configured schema."),
        GitHubError.WorkflowConcurrencyLimit => new("workflow_concurrency_limit", "Workflow concurrency limit was reached.", true, "Wait for the active dispatch to finish, then retry."),
        GitHubError.WorkflowRunNotFound => new("workflow_run_not_found", "Workflow run was not found."),
        GitHubError.WorkflowRunCorrelationFailed => new("workflow_run_correlation_failed", "The dispatched workflow run could not be identified uniquely.", false, "List workflow runs and identify the run manually before dispatching again."),
        GitHubError.BranchNotFound => new("branch_not_found", "Branch was not found."),
        GitHubError.BranchAlreadyExists => new("branch_already_exists", "Branch already exists."),
        GitHubError.BranchNotAllowed => new("branch_not_allowed", "Branch is not allowed by repository policy."),
        GitHubError.ProtectedBranch => new("protected_branch", "Protected branch cannot be deleted."),
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
        GitHubError.MergeabilityCalculating => new("mergeability_calculating", "GitHub is still calculating pull request mergeability.", true, "Wait briefly, then get the pull request again.")
        {
            Status = GitHubMergeabilityStatus.CalculatingRetryable,
            RetryAfterSeconds = 2,
        },
        GitHubError.MergeabilityUnknownRetryable => new("mergeability_unknown", "Pull request mergeability is temporarily unknown.", true, "Wait briefly, then get the pull request again.")
        {
            Status = GitHubMergeabilityStatus.UnknownRetryable,
            RetryAfterSeconds = 2,
        },
        GitHubError.PullRequestBlocked => new("pull_request_blocked", "Pull request merge is blocked by repository requirements.")
        {
            Status = GitHubMergeabilityStatus.Blocked,
        },
        GitHubError.PullRequestRouteNotAllowed => new("pull_request_route_not_allowed", "Pull request route is not allowed."),
        GitHubError.PullRequestStateNotAllowed => new("pull_request_state_not_allowed", "A merged pull request cannot be closed or reopened."),
        GitHubError.PullRequestCommentInvalid => new("pull_request_comment_invalid", "Pull request comment body is invalid."),
        GitHubError.PullRequestReviewInvalid => new("pull_request_review_invalid", "Pull request review input or state is invalid."),
        GitHubError.TagNotFound => new("tag_not_found", "Tag was not found."),
        GitHubError.TagInvalid => new("tag_invalid", "Tag name does not match the allowed pattern."),
        GitHubError.TagAlreadyExists => new("tag_already_exists", "Tag already exists."),
        GitHubError.TagTargetNotAllowed => new("tag_target_not_allowed", "Tag target branch is not allowed."),
        GitHubError.TagDeleteFailed => new("tag_delete_failed", "Tag could not be deleted."),
        GitHubError.ReleaseAlreadyExists => new("release_already_exists", "A release already exists for the tag."),
        GitHubError.ReleaseNotFound => new("release_not_found", "Release was not found."),
        GitHubError.ReleaseAssetInvalid => new("release_asset_invalid", "Release assets must be unique MSI, ZIP, SHA-256, SHA256SUMS.txt, or PowerShell files under the repository root."),
        GitHubError.ReleaseAssetNotFound => new("release_asset_not_found", "A release asset file was not found."),
        GitHubError.ReleaseAssetAlreadyExists => new("release_asset_already_exists", "A release asset with the same name already exists."),
        GitHubError.ReleaseUploadFailed => new("release_upload_failed", "A release asset could not be uploaded; the release remains a draft."),
        GitHubError.NetworkError => new("network_error", "Network error occurred while calling GitHub."),
        GitHubError.Timeout => new("timeout", "GitHub API call timed out."),
        _ => new("github_api_error", "GitHub operation failed."),
    };
}
