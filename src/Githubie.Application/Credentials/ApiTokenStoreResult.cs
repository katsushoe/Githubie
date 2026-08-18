namespace Githubie.Application.Credentials;

/// <summary>
/// トークン保存/削除操作の結果を表します。
/// </summary>
public sealed record ApiTokenStoreResult(bool IsSuccess, ApiTokenStoreError? Error)
{
    public static ApiTokenStoreResult Success() => new(true, null);

    public static ApiTokenStoreResult Failure(ApiTokenStoreError error) => new(false, error);
}

/// <summary>
/// トークン読み出し操作の結果を表します。値そのものは呼び出し側が使用後に速やかに消去してください。
/// </summary>
public sealed record ApiTokenStoreReadResult(bool IsSuccess, char[]? Token, ApiTokenStoreError? Error)
{
    public static ApiTokenStoreReadResult Success(char[] token) => new(true, token, null);

    public static ApiTokenStoreReadResult Failure(ApiTokenStoreError error) => new(false, null, error);
}

/// <summary>
/// トークンストア操作のエラーコードです。
/// </summary>
public enum ApiTokenStoreError
{
    InvalidRepositoryId,
    TokenEmpty,
    TokenTooLong,
    TokenNotFound,
    PlatformNotSupported,
    AccessDenied,
    IoError,
}
