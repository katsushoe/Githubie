using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Githubie.Infrastructure.Credentials;

/// <summary>ユーザーホスト専用です。LocalMachine形式へのフォールバックは行いません。</summary>
[SupportedOSPlatform("windows")]
public sealed class CurrentUserDpapiProtector : IDpapiProtector
{
    private static readonly byte[] Format = "Githubie.UserDPAPI.v2\0"u8.ToArray();

    public byte[] Protect(byte[] plainBytes, byte[] entropy) =>
        [.. Format, .. ProtectedData.Protect(plainBytes, entropy, DataProtectionScope.CurrentUser)];

    public byte[] Unprotect(byte[] protectedBytes, byte[] entropy)
    {
        if (!protectedBytes.AsSpan().StartsWith(Format))
            throw new CryptographicException("Unsupported user credential format.");
        return ProtectedData.Unprotect(protectedBytes[Format.Length..], entropy, DataProtectionScope.CurrentUser);
    }
}
