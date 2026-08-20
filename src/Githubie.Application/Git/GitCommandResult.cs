namespace Githubie.Application.Git;

/// <summary>
/// 低レベルGitコマンド実行の結果を表します。
/// </summary>
public sealed record GitCommandResult(bool IsSuccess, string StandardOutput, string StandardError, GitCommandFailure? Failure)
{
    public static GitCommandResult Success(string standardOutput) => new(true, standardOutput, string.Empty, null);

    public static GitCommandResult Failed(GitCommandFailure failure, string standardOutput = "", string standardError = "") =>
        new(false, standardOutput, standardError, failure);
}

/// <summary>
/// Gitコマンド実行の失敗要因です。
/// </summary>
public enum GitCommandFailure
{
    NotFound,
    Failed,
    TimedOut,
    Cancelled,
}
