using System.Collections;

namespace Githubie.Infrastructure.Git;

/// <summary>
/// Gitへ渡す環境変数から、認証・Askpass関連の危険な既存値を除去し、既定値を強制します。
/// 親プロセスの`GIT_ASKPASS`等がそのまま継承されると、Githubieが意図しない資格情報経路が
/// 使われる可能性があるため、常にGithubie自身が生成した値だけを使わせます。
/// </summary>
public static class GitEnvironmentSanitizer
{
    private static readonly string[] DangerousPrefixes = ["GIT_", "SSH_ASKPASS"];

    public static Dictionary<string, string> BuildBaseEnvironment()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = (string)entry.Key;
            if (DangerousPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            result[key] = (string?)entry.Value ?? string.Empty;
        }

        result["GIT_TERMINAL_PROMPT"] = "0";
        result["LC_ALL"] = "C";

        return result;
    }
}
