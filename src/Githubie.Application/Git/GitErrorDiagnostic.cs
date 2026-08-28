using System.Text.RegularExpressions;

namespace Githubie.Application.Git;

/// <summary>
/// Git標準エラーから秘密値を除去した診断要約を生成します。
/// </summary>
public static partial class GitErrorDiagnostic
{
    private const int MaxLength = 2048;
    private const string Redacted = "[REDACTED]";

    public static string? Sanitize(string standardError)
    {
        if (string.IsNullOrWhiteSpace(standardError))
            return null;

        var diagnostic = UrlUserInfoRegex().Replace(standardError, "${scheme}" + Redacted + "@");
        diagnostic = GitHubTokenRegex().Replace(diagnostic, Redacted);
        diagnostic = AuthorizationRegex().Replace(diagnostic, "${name}" + Redacted);
        diagnostic = SecretAssignmentRegex().Replace(diagnostic, "${name}" + Redacted);
        diagnostic = diagnostic.Trim();

        return diagnostic.Length <= MaxLength ? diagnostic : diagnostic[..MaxLength] + "...";
    }

    [GeneratedRegex(@"(?<scheme>https?://)[^\s/@:]+:[^\s/@]+@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlUserInfoRegex();

    [GeneratedRegex(@"\b(?:gh[opusr]_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]{20,})\b", RegexOptions.CultureInvariant)]
    private static partial Regex GitHubTokenRegex();

    [GeneratedRegex(@"(?<name>\b(?:authorization|proxy-authorization)\s*:\s*(?:bearer|basic)\s+)[^\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationRegex();

    [GeneratedRegex(@"(?<name>\b(?:password|passwd|token|access_token|api_key)\s*[=:]\s*)[^\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretAssignmentRegex();
}
