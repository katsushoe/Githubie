namespace Githubie.Application.Git;

/// <summary>
/// GIT_ASKPASS実行ファイル(Githubie.AskPass)へ渡す、非secretな環境変数を定義します。
/// Personal Access Token本体はコマンドライン引数・環境変数へ含めず、AskPass側がRepository IDを鍵に
/// <see cref="Githubie.Application.Credentials.IApiTokenStore"/>から都度読み出します。
/// </summary>
public static class GitAskPassProtocol
{
    public const string AskPassVariable = "GIT_ASKPASS";
    public const string AskPassRequireVariable = "GIT_ASKPASS_REQUIRE";
    public const string RepositoryIdVariable = "GITHUBIE_ASKPASS_REPOSITORY";

    /// <summary>
    /// Git Credential over HTTPSのUsernameプロンプトへ固定で返す値です。
    /// GitHubはPersonal Access Token認証においてUsernameを問わないため、慣習的な固定値を用います。
    /// </summary>
    public const string FixedUsername = "x-access-token";

    public static IReadOnlyDictionary<string, string> CreateEnvironment(string askPassExecutablePath, string repositoryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(askPassExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryId);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AskPassVariable] = askPassExecutablePath,
            [AskPassRequireVariable] = "force",
            [RepositoryIdVariable] = repositoryId,
        };
    }
}
