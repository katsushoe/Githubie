namespace Githubie.Application.Repositories;

/// <summary>対話承認付きRepository登録を提供します。</summary>
public interface IRepositoryRegistrationService
{
    Task<RepositoryRegistrationResult> RegisterAsync(
        RepositoryRegistrationRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// 対話承認後にGitHub上へRepositoryを新規作成します。`LocalRoot`を指定した場合は、
    /// 未設定の`origin`へ作成先を設定し、承認とToken入力を繰り返さずに登録まで行います。
    /// </summary>
    Task<RepositoryCreateResult> CreateAsync(
        RepositoryCreateRequest request,
        CancellationToken cancellationToken);
}

/// <summary>GitHub Repository新規作成要求です。`Visibility`は`private`（既定）または`public`です。</summary>
public sealed record RepositoryCreateRequest(
    string Repository,
    string Owner,
    string Name,
    string? Visibility,
    string? Description,
    string? LocalRoot,
    string? DevelopBranch = null,
    string? MainBranch = null,
    string? CommitAuthorName = null,
    string? CommitAuthorEmail = null);

/// <summary>GitHub Repository新規作成の結果です。</summary>
public sealed record RepositoryCreateInfo(
    string RepositoryId,
    string GitHubOwner,
    string GitHubRepo,
    bool Private,
    string HtmlUrl,
    bool Registered,
    string? LocalRoot,
    string? Remote,
    string TokenStatus);

/// <summary>GitHub Repository新規作成処理の結果です。`Diagnostic`はGitHubの拒否理由です。</summary>
public sealed record RepositoryCreateResult(
    RepositoryCreateInfo? Value,
    RepositoryRegistrationError? Error,
    string? Diagnostic = null)
{
    public bool IsSuccess => Value is not null && Error is null;

    public static RepositoryCreateResult Success(RepositoryCreateInfo value) => new(value, null);

    public static RepositoryCreateResult Failure(RepositoryRegistrationError error, string? diagnostic = null) =>
        new(null, error, diagnostic);
}

/// <summary>Repository登録要求です。</summary>
public sealed record RepositoryRegistrationRequest(
    string Repository,
    string LocalRoot,
    string? Remote,
    string? DevelopBranch,
    string? MainBranch,
    string? CommitAuthorName = null,
    string? CommitAuthorEmail = null);

/// <summary>Repository登録結果です。</summary>
public sealed record RepositoryRegistrationInfo(
    bool Approved,
    string RepositoryId,
    string GitHubOwner,
    string GitHubRepo,
    string LocalRoot,
    string Remote,
    string DevelopBranch,
    string MainBranch,
    bool TokenConfigured,
    string TokenStatus);

/// <summary>Repository登録の固定エラーです。</summary>
public enum RepositoryRegistrationError
{
    InvalidRepositoryId,
    DuplicateRepositoryId,
    InvalidLocalRoot,
    GitMetadataNotFound,
    ReparsePointDetected,
    InvalidRemote,
    NonGitHubRemote,
    RemoteHttpsRequired,
    GitFailed,
    InvalidAuthorIdentity,
    ApprovalDenied,
    ApprovalTimedOut,
    ApprovalUnavailable,
    PersistenceFailed,
    /// <summary>GitHubのowner名またはRepository名が不正。</summary>
    InvalidGitHubName,
    /// <summary>公開範囲がprivate/public以外。</summary>
    InvalidVisibility,
    /// <summary>ローカルRepositoryに作成先remoteが既に設定されている。</summary>
    RemoteAlreadyConfigured,
    /// <summary>Tokenが保存されておらず、入力もされなかった。</summary>
    TokenUnavailable,
    /// <summary>GitHub Tokenが無効。</summary>
    GitHubAuthenticationFailed,
    /// <summary>Tokenにowner配下へRepositoryを作成する権限がない。</summary>
    GitHubPermissionDenied,
    /// <summary>同名のRepositoryがGitHub上に既に存在する。</summary>
    GitHubRepositoryAlreadyExists,
    /// <summary>その他のGitHub API失敗。</summary>
    GitHubFailed,
}

/// <summary>Repository登録処理の結果です。</summary>
public sealed record RepositoryRegistrationResult(
    RepositoryRegistrationInfo? Value,
    RepositoryRegistrationError? Error)
{
    public bool IsSuccess => Value is not null && Error is null;

    public static RepositoryRegistrationResult Success(RepositoryRegistrationInfo value) => new(value, null);

    public static RepositoryRegistrationResult Failure(RepositoryRegistrationError error) => new(null, error);
}
