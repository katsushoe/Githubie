using FluentAssertions;
using Githubie.Application.Git;
using Githubie.Application.GitHub;
using Githubie.Application.Repositories;
using Xunit;

namespace Githubie.Server.Tests;

/// <summary>
/// GitGatewayError / GitHubError の全列挙値が、意図した固定コードへマッピングされることを
/// 網羅的に固定するテストです。実データ検証で発見したマッピング漏れ(branch_not_found /
/// tag_not_found)の再発防止として、新しいエラーコードを追加した際は本テストの期待値も
/// 更新してください。
/// </summary>
public sealed class GithubieToolResultMapperTests
{
    private static readonly IReadOnlyDictionary<GitGatewayError, string> ExpectedGitGatewayCodes = new Dictionary<GitGatewayError, string>
    {
        [GitGatewayError.RepositoryNotFound] = "repository_not_found",
        [GitGatewayError.RepositoryNotAllowed] = "repository_not_allowed",
        [GitGatewayError.LocalRootNotFound] = "local_root_not_found",
        [GitGatewayError.GitMetadataNotFound] = "git_metadata_not_found",
        [GitGatewayError.ReparsePointDetected] = "reparse_point_detected",
        [GitGatewayError.RemoteMismatch] = "remote_mismatch",
        [GitGatewayError.RemoteHttpsRequired] = "remote_https_required",
        [GitGatewayError.GitNotFound] = "git_not_found",
        [GitGatewayError.GitFailed] = "git_failed",
        [GitGatewayError.GitTimedOut] = "timeout",
        [GitGatewayError.GitCancelled] = "git_failed",
        [GitGatewayError.NetworkError] = "network_error",
        [GitGatewayError.RemoteUnavailable] = "remote_unavailable",
        [GitGatewayError.AuthenticationFailed] = "authentication_failed",
        [GitGatewayError.WorkingTreeDirty] = "working_tree_dirty",
        [GitGatewayError.BranchNotAllowed] = "branch_not_allowed",
        [GitGatewayError.ProtectedBranch] = "protected_branch",
        [GitGatewayError.NothingToPush] = "nothing_to_push",
        [GitGatewayError.NonFastForward] = "non_fast_forward",
        [GitGatewayError.InvalidRef] = "invalid_ref",
        [GitGatewayError.DuplicateRef] = "duplicate_ref",
        [GitGatewayError.LeaseConflict] = "lease_conflict",
        [GitGatewayError.AtomicNotSupported] = "atomic_not_supported",
        [GitGatewayError.BranchProtectionDenied] = "branch_protection_denied",
        [GitGatewayError.TokenPermissionDenied] = "token_permission_denied",
        [GitGatewayError.WorkflowPermissionDenied] = "workflow_permission_denied",
        [GitGatewayError.ApprovalDenied] = "approval_denied",
        [GitGatewayError.ApprovalTimedOut] = "approval_timed_out",
        [GitGatewayError.ApprovalUnavailable] = "approval_unavailable",
        [GitGatewayError.PermissionDenied] = "permission_denied",
    };

    private static readonly IReadOnlyDictionary<GitHubError, string> ExpectedGitHubCodes = new Dictionary<GitHubError, string>
    {
        [GitHubError.RepositoryNotFound] = "repository_not_found",
        [GitHubError.RepositoryDescriptionInvalid] = "repository_description_invalid",
        [GitHubError.WorkflowNotAllowed] = "workflow_not_allowed",
        [GitHubError.WorkflowRefNotAllowed] = "workflow_ref_not_allowed",
        [GitHubError.WorkflowInputInvalid] = "workflow_input_invalid",
        [GitHubError.WorkflowConcurrencyLimit] = "workflow_concurrency_limit",
        [GitHubError.WorkflowRunNotFound] = "workflow_run_not_found",
        [GitHubError.WorkflowRunCorrelationFailed] = "workflow_run_correlation_failed",
        [GitHubError.BranchNotFound] = "branch_not_found",
        [GitHubError.BranchAlreadyExists] = "branch_already_exists",
        [GitHubError.BranchNotAllowed] = "branch_not_allowed",
        [GitHubError.ProtectedBranch] = "protected_branch",
        [GitHubError.AuthenticationFailed] = "authentication_failed",
        [GitHubError.PermissionDenied] = "permission_denied",
        [GitHubError.TokenScopeMissing] = "token_scope_missing",
        [GitHubError.ApiError] = "github_api_error",
        [GitHubError.RateLimited] = "rate_limited",
        [GitHubError.SecondaryRateLimited] = "secondary_rate_limited",
        [GitHubError.InvalidResponse] = "github_api_error",
        [GitHubError.PullRequestNotFound] = "pull_request_not_found",
        [GitHubError.PullRequestNotOpen] = "pull_request_not_open",
        [GitHubError.PullRequestNotMergeable] = "pull_request_not_mergeable",
        [GitHubError.MergeabilityCalculating] = "mergeability_calculating",
        [GitHubError.MergeabilityUnknownRetryable] = "mergeability_unknown",
        [GitHubError.PullRequestBlocked] = "pull_request_blocked",
        [GitHubError.PullRequestRouteNotAllowed] = "pull_request_route_not_allowed",
        [GitHubError.PullRequestStateNotAllowed] = "pull_request_state_not_allowed",
        [GitHubError.PullRequestCommentInvalid] = "pull_request_comment_invalid",
        [GitHubError.PullRequestReviewInvalid] = "pull_request_review_invalid",
        [GitHubError.TagNotFound] = "tag_not_found",
        [GitHubError.TagInvalid] = "tag_invalid",
        [GitHubError.TagAlreadyExists] = "tag_already_exists",
        [GitHubError.TagTargetNotAllowed] = "tag_target_not_allowed",
        [GitHubError.TagDeleteFailed] = "tag_delete_failed",
        [GitHubError.ReleaseAlreadyExists] = "release_already_exists",
        [GitHubError.ReleaseNotFound] = "release_not_found",
        [GitHubError.ReleaseAssetInvalid] = "release_asset_invalid",
        [GitHubError.ReleaseAssetNotFound] = "release_asset_not_found",
        [GitHubError.ReleaseAssetAlreadyExists] = "release_asset_already_exists",
        [GitHubError.ReleaseUploadFailed] = "release_upload_failed",
        [GitHubError.NetworkError] = "network_error",
        [GitHubError.Timeout] = "timeout",
    };

    [Fact]
    public void ExpectedGitGatewayCodes_CoversEveryEnumValue()
    {
        // 列挙値が追加されたのにこのテストの期待値表を更新し忘れた場合に検出する。
        var allValues = Enum.GetValues<GitGatewayError>();
        ExpectedGitGatewayCodes.Keys.Should().BeEquivalentTo(allValues);
    }

    [Fact]
    public void ExpectedGitHubCodes_CoversEveryEnumValue()
    {
        var allValues = Enum.GetValues<GitHubError>();
        ExpectedGitHubCodes.Keys.Should().BeEquivalentTo(allValues);
    }

    [Theory]
    [MemberData(nameof(GitGatewayErrorValues))]
    public void Map_GitGatewayError_ProducesExpectedCode(GitGatewayError error)
    {
        var result = GitGatewayResult<Application.Git.Unit>.Failure(error);

        var mapped = GithubieToolResultMapper.Map("op", "repo", result);

        mapped.Ok.Should().BeFalse();
        mapped.Error!.Code.Should().Be(ExpectedGitGatewayCodes[error]);
    }

    [Theory]
    [MemberData(nameof(GitHubErrorValues))]
    public void Map_GitHubError_ProducesExpectedCode(GitHubError error)
    {
        var result = GitHubResult<string>.Failure(error);

        var mapped = GithubieToolResultMapper.Map("op", "repo", result);

        mapped.Ok.Should().BeFalse();
        mapped.Error!.Code.Should().Be(ExpectedGitHubCodes[error]);
    }

    [Fact]
    public void Map_MergeabilityCalculating_ProducesRetryableStatusAndDelay()
    {
        var mapped = GithubieToolResultMapper.Map(
            "github_pr_merge", "repo", GitHubResult<string>.Failure(GitHubError.MergeabilityCalculating));

        mapped.Error!.Code.Should().Be("mergeability_calculating");
        mapped.Error.Status.Should().Be(GitHubMergeabilityStatus.CalculatingRetryable);
        mapped.Error.Retryable.Should().BeTrue();
        mapped.Error.RetryAfterSeconds.Should().Be(2);
    }

    [Fact]
    public void Map_RegistrationSshRemote_ProducesHttpsRequiredCode()
    {
        var result = RepositoryRegistrationResult.Failure(RepositoryRegistrationError.RemoteHttpsRequired);

        var mapped = GithubieToolResultMapper.Map("register", "repo", result);

        mapped.Error!.Code.Should().Be("remote_https_required");
    }

    [Fact]
    public void Map_UnclassifiedGitFailure_ContainsDiagnosticContract()
    {
        var result = GitGatewayResult<Application.Git.Unit>.Failure(
            GitGatewayError.GitFailed,
            "fatal: remote rejected the update",
            128) with
        {
            CorrelationId = "0123456789abcdef0123456789abcdef",
        };

        var mapped = GithubieToolResultMapper.Map("fetch", "repo", result);

        mapped.Error!.Summary.Should().NotBeNullOrWhiteSpace();
        mapped.Error.SuggestedAction.Should().NotBeNullOrWhiteSpace();
        mapped.Error.CorrelationId.Should().Be(result.CorrelationId);
        mapped.Error.Retryable.Should().BeFalse();
        mapped.Error.Diagnostic.Should().Be(result.Diagnostic);
        mapped.Error.ExitCode.Should().Be(result.ExitCode);
    }

    public static IEnumerable<object[]> GitGatewayErrorValues() =>
        Enum.GetValues<GitGatewayError>().Select(v => new object[] { v });

    public static IEnumerable<object[]> GitHubErrorValues() =>
        Enum.GetValues<GitHubError>().Select(v => new object[] { v });
}
