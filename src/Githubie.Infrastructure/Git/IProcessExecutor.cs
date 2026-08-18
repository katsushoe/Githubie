using Githubie.Application.Git;

namespace Githubie.Infrastructure.Git;

/// <summary>
/// 外部プロセス実行を抽象化します（テスト容易性のための分離）。
/// 呼び出し側は常に固定の実行ファイル名・引数配列(ArgumentList)を渡し、Shellを経由しません。
/// </summary>
public interface IProcessExecutor
{
    Task<GitCommandResult> ExecuteAsync(
        string workingDirectory,
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environmentOverrides,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
