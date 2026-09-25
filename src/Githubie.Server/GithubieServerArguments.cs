namespace Githubie.Server;

/// <summary>
/// Githubie.Serverの起動引数です。既定は単体動作モードで、`--moyai`指定時だけMoyai連携モードになります。
/// </summary>
public sealed record GithubieServerArguments(string? ConfigPath, bool MoyaiIntegration, string[] HostArguments)
{
    public const string MoyaiOption = "--moyai";

    /// <summary>最初の位置引数を設定Pathとし、`--moyai`を取り除いた残りをHostへ渡します。</summary>
    public static GithubieServerArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? configPath = null;
        var moyai = false;
        var hostArguments = new List<string>();
        foreach (var argument in args)
        {
            if (string.Equals(argument, MoyaiOption, StringComparison.OrdinalIgnoreCase))
            {
                moyai = true;
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

        return new GithubieServerArguments(configPath, moyai, hostArguments.ToArray());
    }
}
