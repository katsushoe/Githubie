namespace Githubie.Application.Configuration;

/// <summary>
/// install-root配下の標準ディレクトリ構成を表します。
/// </summary>
public sealed record GithubiePathLayout(
    string InstallRoot,
    string BinDirectory,
    string ConfigDirectory,
    string LogsDirectory,
    string DataDirectory,
    string RepositoryDatabasePath,
    string ServiceStatePath,
    string SecretsDirectory)
{
    /// <summary>ログオンユーザーの可変データをバイナリから分離します。旧Serviceデータは参照しません。</summary>
    public static GithubiePathLayout ForCurrentUser(string binDirectory) =>
        FromUserProfileRoot(binDirectory, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Akatsukisoft", "Githubie"));

    /// <summary>明示されたユーザープロフィール用の配置です。テスト用の隔離ディレクトリにも使用します。</summary>
    public static GithubiePathLayout FromUserProfileRoot(string binDirectory, string profileRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileRoot);
        var root = Path.GetFullPath(profileRoot);
        var data = Path.Combine(root, "data");
        return new GithubiePathLayout(Path.GetFullPath(Path.Combine(binDirectory, "..")),
            Path.GetFullPath(binDirectory), Path.Combine(root, "config"), Path.Combine(root, "logs"),
            data, Path.Combine(data, "githubie.db"), Path.Combine(data, "host-state.json"), Path.Combine(data, "secrets"));
    }

    /// <summary>
    /// 実行アセンブリのディレクトリからinstall-rootを導出します（bin配下からの相対配置を前提とする）。
    /// </summary>
    public static GithubiePathLayout FromBinDirectory(string binDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binDirectory);

        var installRoot = Path.GetFullPath(Path.Combine(binDirectory, ".."));
        var dataDirectory = Path.Combine(installRoot, "data");

        return new GithubiePathLayout(
            InstallRoot: installRoot,
            BinDirectory: Path.GetFullPath(binDirectory),
            ConfigDirectory: Path.Combine(installRoot, "config"),
            LogsDirectory: Path.Combine(installRoot, "logs"),
            DataDirectory: dataDirectory,
            RepositoryDatabasePath: Path.Combine(dataDirectory, "githubie.db"),
            ServiceStatePath: Path.Combine(dataDirectory, "service-state.json"),
            SecretsDirectory: Path.Combine(dataDirectory, "secrets"));
    }
}
