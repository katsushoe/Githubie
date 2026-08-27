namespace Githubie.Application.Configuration;

/// <summary>
/// 設定読み込みの結果を表します。
/// </summary>
public sealed record ConfigurationLoadResult(GithubieOptions? Options, IReadOnlyList<ConfigurationError> Errors)
{
    public bool IsSuccess => Options is not null && Errors.Count == 0;

    public static ConfigurationLoadResult Success(GithubieOptions options) => new(options, []);

    public static ConfigurationLoadResult Failure(IReadOnlyList<ConfigurationError> errors) => new(null, errors);

    public static ConfigurationLoadResult Failure(ConfigurationError error) => new(null, [error]);
}

/// <summary>
/// 個々の設定エラーを、対象箇所(Path)とあわせて表します。
/// </summary>
public sealed record ConfigurationError(ConfigurationErrorCode Code, string Path, string Message);

/// <summary>
/// 設定エラーコードです。
/// </summary>
public enum ConfigurationErrorCode
{
    InvalidJson,
    MissingProperty,
    InvalidMcpPort,
    InvalidMcpPath,
    DuplicateRepositoryId,
    InvalidRepositoryId,
    InvalidGitHubOwner,
    InvalidGitHubRepo,
    InvalidLocalRoot,
    InvalidBranchName,
    InvalidTagPattern,
    InvalidMergeMethod,
    InvalidWorkflowPolicy,
    UnknownProperty,
}
