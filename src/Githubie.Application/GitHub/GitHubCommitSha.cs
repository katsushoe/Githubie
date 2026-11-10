namespace Githubie.Application.GitHub;

/// <summary>GitHubの現行SHA-1契約の完全なコミット識別子を検証します。</summary>
public static class GitHubCommitSha
{
    /// <summary>省略や省略形を含まない40桁の16進表記かを返します。</summary>
    public static bool IsValid(string? value) =>
        value is { Length: 40 } && value.All(Uri.IsHexDigit);
}
