namespace Githubie.Application.Repositories;

/// <summary>
/// Repository境界検証の結果を表します。
/// </summary>
public sealed record RepositoryValidationResult(bool IsAllowed, RepositoryValidationError? Error)
{
    public static RepositoryValidationResult Allowed() => new(true, null);

    public static RepositoryValidationResult Denied(RepositoryValidationError error) => new(false, error);
}

/// <summary>
/// Repository境界検証のエラーコードです。
/// </summary>
public enum RepositoryValidationError
{
    RepositoryNotFound,
    RepositoryNotAllowed,
    LocalRootMismatch,
    LocalRootNotFound,
    GitMetadataNotFound,
    ReparsePointDetected,
    RemoteMismatch,
}
