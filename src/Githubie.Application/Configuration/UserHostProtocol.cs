namespace Githubie.Application.Configuration;

/// <summary>ユーザーHTTP接続で共通に使用する公開プロトコル名です。資格そのものは設定に埋め込みません。</summary>
public static class UserHostProtocol
{
    public const string CredentialHeader = "X-Githubie-Session";
}
