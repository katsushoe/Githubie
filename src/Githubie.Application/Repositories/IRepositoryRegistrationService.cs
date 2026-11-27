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

    /// <summary>
    /// 登録済みRepositoryのLocal Rootだけを対話承認後に変更し、実行中サービスへ即時反映します。
    /// 新しいLocal Rootは実在・Git管理・reparse pointなしで、登録remote（空なら一致するHTTPS remote）が
    /// 登録済みのGitHub Repositoryを指す必要があります。Token・Policy・作者設定・Moyai対応付けは保持します。
    /// </summary>
    Task<RepositoryRelocateResult> RelocateAsync(
        string repository,
        string localRoot,
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
    string? CommitAuthorEmail = null,
    Guid? MoyaiProjectId = null);

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
    string TokenStatus)
{
    /// <summary>対応付けたMoyai Project UUIDです。</summary>
    public Guid? MoyaiProjectId { get; init; }
}

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
    /// <summary>Moyai Project UUIDが空。</summary>
    InvalidMoyaiProjectId,
    /// <summary>Moyai Project UUIDがほかのRepositoryで使用中、または管理者設定と異なる。</summary>
    DuplicateMoyaiProjectId,
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
    /// <summary>Repositoryが未登録。</summary>
    RepositoryNotRegistered,
    /// <summary>新しいLocal Rootのremoteが登録済みGitHub Repositoryと一致しない。</summary>
    RemoteMismatch,
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

/// <summary>Local Root変更の結果情報です。</summary>
public sealed record RepositoryRelocateInfo(
    bool Approved,
    string RepositoryId,
    string PreviousLocalRoot,
    string LocalRoot,
    string Remote);

/// <summary>Local Root変更処理の結果です。</summary>
public sealed record RepositoryRelocateResult(
    RepositoryRelocateInfo? Value,
    RepositoryRegistrationError? Error)
{
    public bool IsSuccess => Value is not null && Error is null;

    public static RepositoryRelocateResult Success(RepositoryRelocateInfo value) => new(value, null);

    public static RepositoryRelocateResult Failure(RepositoryRegistrationError error) => new(null, error);
}
