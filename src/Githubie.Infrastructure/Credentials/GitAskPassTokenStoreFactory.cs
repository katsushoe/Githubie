using System.Runtime.Versioning;
using Githubie.Application.Configuration;
using Githubie.Application.Credentials;

namespace Githubie.Infrastructure.Credentials;

/// <summary>
/// Githubie.AskPassが読むToken保存先を、起動元ホストが渡した資格DBの指定から選びます。
/// ログオンユーザーホストはユーザープロフィールのSQLite資格DB、旧Service配置は実行ファイル基準の
/// <c>data\secrets</c>を使います。指定が不正な場合は旧配置へ切り替えず、nullで失敗させます。
/// </summary>
[SupportedOSPlatform("windows")]
public static class GitAskPassTokenStoreFactory
{
    public static IApiTokenStore? Create(string binDirectory, string? userCredentialDatabasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binDirectory);
        if (userCredentialDatabasePath is null)
            return new DpapiFileTokenStore(GithubiePathLayout.FromBinDirectory(binDirectory).SecretsDirectory);

        if (string.IsNullOrWhiteSpace(userCredentialDatabasePath)
            || !Path.IsPathFullyQualified(userCredentialDatabasePath)
            || !File.Exists(userCredentialDatabasePath))
            return null;

        return new SqliteUserCredentialStore(userCredentialDatabasePath, new CurrentUserDpapiProtector());
    }
}
