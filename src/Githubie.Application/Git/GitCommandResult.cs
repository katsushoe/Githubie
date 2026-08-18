namespace Githubie.Application.Git;

/// <summary>
/// 低レベルGitコマンド実行の結果を表します。
/// </summary>
public sealed record GitCommandResult(bool IsSuccess, string StandardOutput, GitCommandFailure? Failure)
{
    public static GitCommandResult Success(string standardOutput) => new(true, standardOutput, null);

    public static GitCommandResult Failed(GitCommandFailure failure, string standardOutput = "") => new(false, standardOutput, failure);
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
