using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Githubie.Infrastructure.Credentials;

/// <summary>
/// Windows DPAPI(LocalMachineスコープ)を用いる<see cref="IDpapiProtector"/>実装です。
/// LocalMachineスコープを用いるのは、Windows Service(LocalSystem)からも復号できる必要があるためです。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiProtector : IDpapiProtector
{
    public byte[] Protect(byte[] plainBytes, byte[] entropy) =>
        ProtectedData.Protect(plainBytes, entropy, DataProtectionScope.LocalMachine);

    public byte[] Unprotect(byte[] protectedBytes, byte[] entropy) =>
        ProtectedData.Unprotect(protectedBytes, entropy, DataProtectionScope.LocalMachine);
}
