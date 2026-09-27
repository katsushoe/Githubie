namespace Githubie.Server;

/// <summary>
/// Githubie.Serverの起動引数です。既定は単体動作モードで、`--moyai`指定時だけMoyai連携モードになります。
/// `--direct-unrestricted`は`--moyai`と同時にだけ指定でき、Authorizationなしのloopback直接接続に
/// すべてのRepository Toolを許可します（`direct_connection=unrestricted`）。
/// </summary>
public sealed record GithubieServerArguments(string? ConfigPath, bool MoyaiIntegration, bool DirectUnrestricted, string[] HostArguments)
{
    public const string MoyaiOption = "--moyai";
    public const string DirectUnrestrictedOption = "--direct-unrestricted";

    /// <summary>最初の位置引数を設定Pathとし、`--moyai`と`--direct-unrestricted`を取り除いた残りをHostへ渡します。</summary>
    public static GithubieServerArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? configPath = null;
        var moyai = false;
        var directUnrestricted = false;
        var hostArguments = new List<string>();
        foreach (var argument in args)
        {
            if (string.Equals(argument, MoyaiOption, StringComparison.OrdinalIgnoreCase))
            {
                moyai = true;
            }
            else if (string.Equals(argument, DirectUnrestrictedOption, StringComparison.OrdinalIgnoreCase))
            {
                directUnrestricted = true;
            }
            else if (configPath is null && !argument.StartsWith('-'))
            {
                configPath = argument;
            }
            else
            {
                hostArguments.Add(argument);
            }
        }

        if (directUnrestricted && !moyai)
            throw new ArgumentException("--direct-unrestricted requires --moyai.", nameof(args));

        return new GithubieServerArguments(configPath, moyai, directUnrestricted, hostArguments.ToArray());
    }
}
