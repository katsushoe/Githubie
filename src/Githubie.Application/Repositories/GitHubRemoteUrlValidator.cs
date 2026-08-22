using System.Text.RegularExpressions;

namespace Githubie.Application.Repositories;

/// <summary>
/// GitリモートURLが`github.com`上の期待した`owner/repo`を指していることを検証します。
/// Agentがremote URLを書き換えて任意サイトへGithubie経由で通信することを防ぎます。
/// </summary>
public static partial class GitHubRemoteUrlValidator
{
    public static bool IsExpectedRemote(string remoteUrl, string owner, string repo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        var parsed = TryParse(remoteUrl);
        if (parsed is null)
        {
            return false;
        }

        return string.Equals(parsed.Value.Owner, owner, StringComparison.OrdinalIgnoreCase)
            && string.Equals(parsed.Value.Repo, repo, StringComparison.OrdinalIgnoreCase);
    }

    public static (string Owner, string Repo)? TryParse(string remoteUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteUrl);

        var httpsMatch = HttpsPattern().Match(remoteUrl);
        if (httpsMatch.Success)
        {
            return (httpsMatch.Groups["owner"].Value, httpsMatch.Groups["repo"].Value);
        }

        return null;
    }

    public static bool IsSshRemote(string remoteUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteUrl);
        return SshPattern().IsMatch(remoteUrl) || SshUriPattern().IsMatch(remoteUrl);
    }

    [GeneratedRegex(@"^https://github\.com/(?<owner>[A-Za-z0-9._-]+)/(?<repo>[A-Za-z0-9._-]+?)(\.git)?/?$", RegexOptions.CultureInvariant)]
    private static partial Regex HttpsPattern();

    [GeneratedRegex(@"^git@github\.com:(?<owner>[A-Za-z0-9._-]+)/(?<repo>[A-Za-z0-9._-]+?)(\.git)?$", RegexOptions.CultureInvariant)]
    private static partial Regex SshPattern();

    [GeneratedRegex(@"^ssh://git@github\.com/(?<owner>[A-Za-z0-9._-]+)/(?<repo>[A-Za-z0-9._-]+?)(\.git)?/?$", RegexOptions.CultureInvariant)]
    private static partial Regex SshUriPattern();
}
