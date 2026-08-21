using FluentAssertions;
using Githubie.Application.Git;
using Githubie.Application.GitHub;
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
        [GitGatewayError.GitNotFound] = "git_not_found",
        [GitGatewayError.GitFailed] = "git_failed",
        [GitGatewayError.GitTimedOut] = "timeout",
        [GitGatewayError.GitCancelled] = "git_failed",
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
        [GitHubError.BranchNotFound] = "branch_not_found",
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
        [GitHubError.PullRequestRouteNotAllowed] = "pull_request_route_not_allowed",
        [GitHubError.PullRequestStateNotAllowed] = "pull_request_state_not_allowed",
        [GitHubError.PullRequestCommentInvalid] = "pull_request_comment_invalid",
        [GitHubError.TagNotFound] = "tag_not_found",
        [GitHubError.TagInvalid] = "tag_invalid",
        [GitHubError.TagAlreadyExists] = "tag_already_exists",
        [GitHubError.TagTargetNotAllowed] = "tag_target_not_allowed",
        [GitHubError.ReleaseAlreadyExists] = "release_already_exists",
        [GitHubError.ReleaseAssetInvalid] = "release_asset_invalid",
        [GitHubError.ReleaseAssetNotFound] = "release_asset_not_found",
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

    public static IEnumerable<object[]> GitGatewayErrorValues() =>
        Enum.GetValues<GitGatewayError>().Select(v => new object[] { v });

    public static IEnumerable<object[]> GitHubErrorValues() =>
        Enum.GetValues<GitHubError>().Select(v => new object[] { v });
}
