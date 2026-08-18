namespace Githubie.Infrastructure.Credentials;

/// <summary>
/// DPAPI暗号化/復号をテスト容易性のために分離したポートです。
/// </summary>
public interface IDpapiProtector
{
    byte[] Protect(byte[] plainBytes, byte[] entropy);

    byte[] Unprotect(byte[] protectedBytes, byte[] entropy);
}
