using Githubie.Application.Configuration;

namespace Githubie.Server;

/// <summary>
/// Moyai Consumer Contractの`integration_mode`と`direct_connection`を、起動時の設定から決定します。
/// 値の名称と意味はBuckettieと共通です。
/// </summary>
public static class GithubieIntegrationMode
{
    public const string Standalone = "standalone";
    public const string Moyai = "moyai";
    public const string ReadOnly = "read_only";
    public const string Unrestricted = "unrestricted";

    /// <summary>単体動作では`standalone`、`--moyai`では`moyai`です。</summary>
    public static string IntegrationMode(GithubieOptions? options) =>
        options?.ProviderAuthentication is null ? Standalone : Moyai;

    /// <summary>単体動作または`--direct-unrestricted`では`unrestricted`、`--moyai`だけなら`read_only`です。</summary>
    public static string DirectConnection(GithubieOptions? options) =>
        options?.ProviderAuthentication is null || options.DirectUnrestricted ? Unrestricted : ReadOnly;
}
