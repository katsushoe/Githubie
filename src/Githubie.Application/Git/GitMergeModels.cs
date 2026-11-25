namespace Githubie.Application.Git;

/// <summary>取り込み元の種類です。呼出側が明示し、Githubieは種類を推測しません。</summary>
public static class GitMergeSourceKind
{
    public const string Branch = "branch";
    public const string RemoteBranch = "remote_branch";
    public const string Tag = "tag";
    public const string Commit = "commit";
}

/// <summary>fast-forwardの扱いです。</summary>
public static class GitMergeFastForwardMode
{
    /// <summary>fast-forwardできる場合はfast-forwardし、できない場合はmerge commitを作ります。</summary>
    public const string Allow = "allow";

    /// <summary>fast-forwardできる場合でも常にmerge commitを作ります。</summary>
    public const string Never = "never";

    /// <summary>fast-forwardだけを許可し、できない場合は拒否します。</summary>
    public const string Only = "only";
}

/// <summary>衝突ファイル1件の明示的な解消方法です。</summary>
/// <param name="Path">衝突一覧に含まれるRepository相対path。</param>
/// <param name="Choice">`current`（現在branch側）、`source`（取り込み元側）、`content`（全文を指定）。</param>
/// <param name="Content">`choice=content`のときの解消済み全文（UTF-8テキスト）。</param>
public sealed record GitMergeResolution(string Path, string Choice, string? Content = null)
{
    public const string Current = "current";
    public const string Source = "source";
    public const string FullContent = "content";
}

/// <summary>Infrastructureへ渡す検証済みのmerge要求です。</summary>
public sealed record GitMergeRequest(
    string Branch,
    string ExpectedHead,
    string SourceRef,
    string ExpectedSourceSha,
    string FastForward,
    string Message,
    string AuthorName,
    string AuthorEmail,
    IReadOnlyList<GitMergeResolution> Resolutions);

/// <summary>作業ツリーとHEADを変更せずに拒否したmerge固有の理由です。</summary>
public enum GitMergeRejection
{
    WorkingTreeDirty,
    SourceNotFound,
    ExpectedSourceMismatch,
    NothingToMerge,
    FastForwardNotPossible,
    Conflict,
    ResolutionInvalid,
}

/// <summary>merge実行の結果です。<see cref="Command"/>が失敗した場合はGitの失敗分類を優先します。</summary>
public sealed record GitMergeCommandOutcome(
    GitCommandResult Command,
    GitMergeRejection? Rejection = null,
    GitRepositoryMergeInfo? Info = null,
    IReadOnlyList<string>? Conflicts = null)
{
    public static GitMergeCommandOutcome Rejected(GitMergeRejection rejection, IReadOnlyList<string>? conflicts = null) =>
        new(GitCommandResult.Success(string.Empty), rejection, null, conflicts);

    public static GitMergeCommandOutcome Failed(GitCommandResult command) => new(command);

    public static GitMergeCommandOutcome Succeeded(GitRepositoryMergeInfo info) =>
        new(GitCommandResult.Success(info.CommitSha), null, info);
}

/// <summary>取り込み後のローカルbranchです。pushは行いません。</summary>
/// <param name="Branch">取り込み先のbranch名。</param>
/// <param name="CommitSha">取り込み後のbranch先頭（merge commitまたはfast-forward先）。</param>
/// <param name="Parents">新しい先頭commitの親SHA。fast-forwardでは取り込み元commitの親です。</param>
/// <param name="FastForward">fast-forwardで更新した場合true。</param>
/// <param name="PreviousHead">取り込み前のbranch先頭。</param>
/// <param name="SourceSha">取り込み元commit。</param>
/// <param name="ResolvedPaths">明示指定で解消した衝突path。</param>
public sealed record GitRepositoryMergeInfo(
    string Branch,
    string CommitSha,
    IReadOnlyList<string> Parents,
    bool FastForward,
    string PreviousHead,
    string SourceSha,
    IReadOnlyList<string> ResolvedPaths);
