using System.Text.RegularExpressions;

namespace Githubie.Domain;

/// <summary>
/// Repositoryごとに許可された操作境界を表します。
/// </summary>
public sealed record RepositoryPolicy(
    string RepositoryId,
    string DevelopBranch,
    string MainBranch,
    IReadOnlySet<string> DirectPushBranches,
    IReadOnlySet<string> PullBranches,
    IReadOnlySet<PullRequestRoute> PullRequestRoutes,
    IReadOnlySet<string> ProtectedBranches,
    string TagTargetBranch,
    string TagPattern,
    bool RequireCleanWorkingTree)
{
    /// <summary>
    /// 現在の状態で直接Pushできるか検証します。
    /// </summary>
    public PolicyResult ValidatePush(string branch, bool workingTreeClean)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);

        if (ProtectedBranches.Contains(branch))
        {
            return PolicyResult.Denied(PolicyErrorCode.ProtectedBranch);
        }

        if (!DirectPushBranches.Contains(branch))
        {
            return PolicyResult.Denied(PolicyErrorCode.BranchNotAllowed);
        }

        return RequireCleanWorkingTree && !workingTreeClean
            ? PolicyResult.Denied(PolicyErrorCode.WorkingTreeDirty)
            : PolicyResult.Allowed();
    }

    /// <summary>
    /// Pull Requestの経路を検証します。
    /// </summary>
    public PolicyResult ValidatePullRequest(string source, string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        return PullRequestRoutes.Contains(new PullRequestRoute(source, destination))
            ? PolicyResult.Allowed()
            : PolicyResult.Denied(PolicyErrorCode.PullRequestRouteNotAllowed);
    }

    /// <summary>
    /// Tag名と対象ブランチを検証します。
    /// </summary>
    public PolicyResult ValidateTag(string tag, string targetBranch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetBranch);

        if (!string.Equals(targetBranch, TagTargetBranch, StringComparison.Ordinal))
        {
            return PolicyResult.Denied(PolicyErrorCode.TagTargetNotAllowed);
        }

        return Regex.IsMatch(tag, TagPattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            ? PolicyResult.Allowed()
            : PolicyResult.Denied(PolicyErrorCode.TagInvalid);
    }
}

/// <summary>
/// 許可されたPull Request経路を表します。
/// </summary>
public sealed record PullRequestRoute(string Source, string Destination);

/// <summary>
/// Policy検証結果を表します。
/// </summary>
public sealed record PolicyResult(bool IsAllowed, PolicyErrorCode? ErrorCode)
{
    /// <summary>許可結果を生成します。</summary>
    public static PolicyResult Allowed() => new(true, null);

    /// <summary>拒否結果を生成します。</summary>
    public static PolicyResult Denied(PolicyErrorCode errorCode) => new(false, errorCode);
}

/// <summary>
/// Repository Policyが返すエラーコードです。
/// </summary>
public enum PolicyErrorCode
{
    WorkingTreeDirty,
    BranchNotAllowed,
    ProtectedBranch,
    PullRequestRouteNotAllowed,
    TagInvalid,
    TagTargetNotAllowed,
}
