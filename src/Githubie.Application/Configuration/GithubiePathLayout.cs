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
