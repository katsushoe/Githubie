namespace Githubie.Application.Configuration;

/// <summary>ユーザーHTTP接続で共通に使用する公開プロトコル名です。資格そのものは設定に埋め込みません。</summary>
public static class UserHostProtocol
{
    public const string CredentialHeader = "X-Githubie-Session";
    public const string GenerationHeader = "X-Githubie-Generation";
    public const string ErrorHeader = "X-Githubie-Error";
    public const string OwnerRejected = "user_host_owner_rejected";
    public const string ProviderId = "githubie";
    public const int DiscoveryVersion = 1;
}
