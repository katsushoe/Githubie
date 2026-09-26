namespace Githubie.Application.GitHub;

/// <summary>
/// GitHub REST API操作の結果を表します。
/// </summary>
public sealed record GitHubResult<T>(bool IsSuccess, T? Value, GitHubError? Error)
{
    /// <summary>GitHubが返した拒否理由などの秘密値を含まない診断情報です。</summary>
    public string? Diagnostic { get; init; }

    /// <summary>失敗したGitHub API呼び出しのHTTP statusです。</summary>
    public int? HttpStatus { get; init; }

    /// <summary>監査ログとMCP応答を対応付ける識別子です。</summary>
    public string? CorrelationId { get; init; }

    public static GitHubResult<T> Success(T value) => new(true, value, null);

    public static GitHubResult<T> Failure(GitHubError error, string? diagnostic = null, int? httpStatus = null) =>
        new(false, default, error) { Diagnostic = diagnostic, HttpStatus = httpStatus };

    /// <summary>別の型の失敗結果から、エラーと診断情報を引き継ぎます。</summary>
    public static GitHubResult<T> FailureFrom<TSource>(GitHubResult<TSource> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(false, default, source.Error)
        {
            Diagnostic = source.Diagnostic,
            HttpStatus = source.HttpStatus,
            CorrelationId = source.CorrelationId,
        };
    }
}

/// <summary>
/// GitHub REST API操作のエラーコードです。
/// </summary>
public enum GitHubError
{
    RepositoryNotFound,
    RepositoryDescriptionInvalid,
    WorkflowNotAllowed,
    WorkflowRefNotAllowed,
    WorkflowInputInvalid,
    WorkflowConcurrencyLimit,
    WorkflowRunNotFound,
    WorkflowRunCorrelationFailed,
    BranchNotFound,
    BranchAlreadyExists,
    BranchNotAllowed,
    ProtectedBranch,

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
    MergeabilityCalculating,
    MergeabilityUnknownRetryable,
    /// <summary>GitHubがmergeableと判定しながらmergeを拒否した。再試行では解消しない。</summary>
    PullRequestMergeRejected,
    PullRequestBlocked,
    PullRequestRouteNotAllowed,
    PullRequestStateNotAllowed,
    PullRequestCommentInvalid,
    PullRequestReviewInvalid,

    IssueNotFound,

    TagNotFound,
    TagInvalid,
    TagAlreadyExists,
    TagTargetNotAllowed,
    TagSourceInvalid,
    TagSourceNotFound,
    TagDeleteFailed,

    ReleaseAlreadyExists,
    ReleaseNotFound,
    ReleaseNotDraft,
    ReleaseAssetInvalid,
    ReleaseAssetNotFound,
    ReleaseAssetAlreadyExists,
    ReleaseUploadFailed,

    NetworkError,
    Timeout,
    BranchSourceInvalid,
    BranchSourceNotFound,
}
