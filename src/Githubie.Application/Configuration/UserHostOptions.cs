namespace Githubie.Application.Configuration;

/// <summary>githubie.json の user_host に保存するホストと自動起動の設定です。</summary>
public sealed record UserHostOptions(
    bool MoyaiIntegration = false,
    bool DirectUnrestricted = false,
    bool AutostartEnabled = true,
    string AuthenticationMode = UserHostAuthenticationMode.Legacy,
    bool StartupUpdateEnabled = true);

/// <summary>旧動作と所有ユーザー認証を明示的に区別します。</summary>
public static class UserHostAuthenticationMode
{
    public const string Legacy = "legacy";
    public const string WindowsOwner = "windows_owner";
    public static bool IsValid(string? mode) => mode is Legacy or WindowsOwner;
}
