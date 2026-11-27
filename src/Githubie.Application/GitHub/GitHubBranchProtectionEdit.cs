namespace Githubie.Application.GitHub;

/// <summary>GitHub側ブランチ保護の明示的な変更。nullは既存値を維持します。</summary>
public sealed record GitHubBranchProtectionEdit(
    bool? RequirePullRequest = null,
    int? RequiredApprovingReviews = null,
    bool? AllowForcePushes = null,
    bool? AllowDeletions = null,
    bool? EnforceAdmins = null,
    bool? RequireStatusChecks = null,
    IReadOnlyList<string>? StatusCheckContexts = null,
    bool? StrictStatusChecks = null)
{
    /// <summary>矛盾する変更は、承認画面やGitHubへの送信より前に拒否します。</summary>
    public bool IsValid()
    {
        if (RequirePullRequest is null && RequiredApprovingReviews is null && AllowForcePushes is null
            && AllowDeletions is null && EnforceAdmins is null && RequireStatusChecks is null
            && StatusCheckContexts is null && StrictStatusChecks is null)
            return false;
        if (RequiredApprovingReviews is < 0 or > 6 || (RequirePullRequest == false && RequiredApprovingReviews is not null))
            return false;
        if (RequireStatusChecks == false && (StatusCheckContexts is not null || StrictStatusChecks is not null))
            return false;
        if (RequireStatusChecks == true && StatusCheckContexts is null)
            return false;
        if (StatusCheckContexts is not null
            && (StatusCheckContexts.Count > 100
                || StatusCheckContexts.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 255 || value.Any(char.IsControl))
                || StatusCheckContexts.Distinct(StringComparer.Ordinal).Count() != StatusCheckContexts.Count))
            return false;
        return true;
    }
}

/// <summary>GitHubのexact branch endpointに渡せる名前だけを受理します。</summary>
public static class GitHubProtectionBranchName
{
    public static bool IsValid(string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch) || branch.Length > 1024 || branch == "@"
            || branch.StartsWith('/') || branch.EndsWith('/') || branch.EndsWith('.')
            || branch.Contains("..", StringComparison.Ordinal) || branch.Contains("@{", StringComparison.Ordinal)
            || branch.Any(character => char.IsControl(character) || character is ' ' or '~' or '^' or ':' or '?' or '*' or '[' or '\\'))
            return false;
        return branch.Split('/').All(part => part.Length > 0 && !part.StartsWith('.')
            && !part.EndsWith(".lock", StringComparison.Ordinal));
    }
}
