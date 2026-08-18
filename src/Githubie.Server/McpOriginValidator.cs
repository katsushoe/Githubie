namespace Githubie.Server;

/// <summary>
/// MCPエンドポイントへのリクエストOriginを検証します。
/// Originヘッダが存在しない場合は非ブラウザクライアントとみなし許可します。
/// 存在する場合はloopback・ポート一致・Query/Fragmentなしを要求し、DNS rebinding等を防ぎます。
/// </summary>
public static class McpOriginValidator
{
    public static bool IsAllowed(string? origin, int expectedPort)
    {
        if (string.IsNullOrEmpty(origin))
        {
            return true;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!uri.IsLoopback)
        {
            return false;
        }

        if (uri.Port != expectedPort)
        {
            return false;
        }

        return uri.Query.Length == 0 && uri.Fragment.Length == 0;
    }
}
