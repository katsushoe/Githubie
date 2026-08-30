namespace Githubie.Application.Repositories;

/// <summary>対話承認付きRepository登録を提供します。</summary>
public interface IRepositoryRegistrationService
{
    Task<RepositoryRegistrationResult> RegisterAsync(
        RepositoryRegistrationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Repository登録要求です。</summary>
public sealed record RepositoryRegistrationRequest(
    string Repository,
    string LocalRoot,
    string? Remote,
    string? DevelopBranch,
    string? MainBranch);

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
    ApprovalDenied,
    ApprovalTimedOut,
    ApprovalUnavailable,
    PersistenceFailed,
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
