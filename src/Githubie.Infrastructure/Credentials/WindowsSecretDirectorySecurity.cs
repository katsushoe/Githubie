using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Githubie.Infrastructure.Credentials;

/// <summary>
/// Secret保存ディレクトリのACLを、継承を切ったうえでLocalSystem/Administrators/現在のユーザーの
/// FullControlのみに限定します。DPAPIで暗号化していても、ファイル自体への読み取りアクセスは
/// OSのACLで最小権限に絞る必要があるため。
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsSecretDirectorySecurity
{
    public static void Ensure(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        Directory.CreateDirectory(directoryPath);

        var directoryInfo = new DirectoryInfo(directoryPath);
        var security = directoryInfo.GetAccessControl();

        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, targetType: typeof(SecurityIdentifier)))
        {
            security.RemoveAccessRule(rule);
        }

        var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, domainSid: null);
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, domainSid: null);

        security.AddAccessRule(CreateFullControlRule(localSystem));
        security.AddAccessRule(CreateFullControlRule(administrators));

        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser is not null && currentUser != localSystem)
        {
            security.AddAccessRule(CreateFullControlRule(currentUser));
        }

        directoryInfo.SetAccessControl(security);
    }

    private static FileSystemAccessRule CreateFullControlRule(SecurityIdentifier identity) => new(
        identity,
        FileSystemRights.FullControl,
        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
        PropagationFlags.None,
        AccessControlType.Allow);
}
