namespace Githubie.Application.GitHub;

/// <summary>
/// GitHub REST API操作の結果を表します。
/// </summary>
public sealed record GitHubResult<T>(bool IsSuccess, T? Value, GitHubError? Error)
{
    public static GitHubResult<T> Success(T value) => new(true, value, null);

    public static GitHubResult<T> Failure(GitHubError error) => new(false, default, error);
}

/// <summary>
/// GitHub REST API操作のエラーコードです。
/// </summary>
public enum GitHubError
{
    RepositoryNotFound,
    BranchNotFound,

    AuthenticationFailed,
    PermissionDenied,
    TokenScopeMissing,

    ApiError,
    RateLimited,
    SecondaryRateLimited,
    InvalidResponse,

    PullRequestNotFound,
    PullRequestNotOpen,
    PullRequestNotMergeable,
    PullRequestRouteNotAllowed,
    PullRequestStateNotAllowed,
    PullRequestCommentInvalid,
    PullRequestReviewInvalid,

    TagNotFound,
    TagInvalid,
    TagAlreadyExists,
    TagTargetNotAllowed,

    ReleaseAlreadyExists,
    ReleaseAssetInvalid,
    ReleaseAssetNotFound,
    ReleaseUploadFailed,

    NetworkError,
    Timeout,
}
