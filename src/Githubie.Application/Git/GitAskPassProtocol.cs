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
    /// ログオンユーザーホストの資格DB（ユーザープロフィール内の<c>githubie.db</c>）の絶対パスです。
    /// 設定時はAskPassがこのDBから現在ユーザーDPAPIで保護されたTokenを読み、未設定時は旧Service配置の
    /// 実行ファイル基準<c>data\secrets</c>を読みます。パスは秘密情報ではありません。
    /// </summary>
    public const string UserCredentialDatabaseVariable = "GITHUBIE_ASKPASS_USER_DATABASE";

    /// <summary>
    /// Git Credential over HTTPSのUsernameプロンプトへ固定で返す値です。
    /// GitHubはPersonal Access Token認証においてUsernameを問わないため、慣習的な固定値を用います。
    /// </summary>
    public const string FixedUsername = "x-access-token";

    public static IReadOnlyDictionary<string, string> CreateEnvironment(
        string askPassExecutablePath, string repositoryId, string? userCredentialDatabasePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(askPassExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryId);
        if (userCredentialDatabasePath is not null && !Path.IsPathFullyQualified(userCredentialDatabasePath))
            throw new ArgumentException("The user credential database path must be fully qualified.", nameof(userCredentialDatabasePath));

        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AskPassVariable] = askPassExecutablePath,
            [AskPassRequireVariable] = "force",
            [RepositoryIdVariable] = repositoryId,
        };
        if (userCredentialDatabasePath is not null)
            environment[UserCredentialDatabaseVariable] = userCredentialDatabasePath;
        return environment;
    }
}
