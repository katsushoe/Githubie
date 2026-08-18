using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using FluentAssertions;
using Githubie.Infrastructure.Credentials;
using Xunit;

namespace Githubie.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsSecretDirectorySecurityTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"githubie-acl-test-{Guid.NewGuid():N}");

    [Fact]
    public void Ensure_DisablesInheritanceAndGrantsCurrentUserFullControl()
    {
        WindowsSecretDirectorySecurity.Ensure(_directory);

        var security = new DirectoryInfo(_directory).GetAccessControl();

        security.AreAccessRulesProtected.Should().BeTrue("実機検証: 継承を切り、明示ACEのみを残す必要がある");

        var currentUser = WindowsIdentity.GetCurrent().User!;
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, targetType: typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();

        rules.Should().Contain(r =>
            ((SecurityIdentifier)r.IdentityReference) == currentUser
            && r.AccessControlType == AccessControlType.Allow
            && r.FileSystemRights.HasFlag(FileSystemRights.FullControl));
    }

    [Fact]
    public void Ensure_IsIdempotent()
    {
        WindowsSecretDirectorySecurity.Ensure(_directory);
        var act = () => WindowsSecretDirectorySecurity.Ensure(_directory);

        act.Should().NotThrow();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
