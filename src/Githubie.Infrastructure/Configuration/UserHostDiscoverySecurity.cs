using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Githubie.Infrastructure.Configuration;

/// <summary>選択されたプロフィールだけのACLとパス境界を検証します。</summary>
[SupportedOSPlatform("windows")]
internal static class UserHostDiscoverySecurity
{
    public static void ValidatePath(string path)
    {
        for (string? current = new FileInfo(path).FullName; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UserHostDiscoveryException("discovery_reparse_rejected");
        }
    }

    public static void ValidatePrivate(string path, string sid, bool directory)
    {
        ValidatePath(path);
        FileSystemSecurity security = directory
            ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner)
            : new FileInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        if (security.GetOwner(typeof(SecurityIdentifier))?.Value != sid
            || (directory && !security.AreAccessRulesProtected))
            throw new UserHostDiscoveryException("discovery_acl_rejected");
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        if (rules.Length == 0 || rules.Any(rule => rule.IdentityReference.Value != sid
            || rule.AccessControlType != AccessControlType.Allow))
            throw new UserHostDiscoveryException("discovery_acl_rejected");
    }
}
