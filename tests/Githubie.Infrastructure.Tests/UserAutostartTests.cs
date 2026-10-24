using FluentAssertions;
using Githubie.Infrastructure.Configuration;
using Xunit;

namespace Githubie.Infrastructure.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class UserAutostartTests
{
    [Fact]
    public void NewProfile_DefaultsOnAndExistingOffSurvivesReinitialization()
    {
        WithPreference((path, registration) =>
        {
            var startup = new UserAutostart(@"C:\Githubie\bin\Githubie.Server.exe", path, registration);
            startup.Initialize().Should().BeTrue();
            registration.Command.Should().Contain("--user-host --autostart");
            startup.SetEnabled(false).Should().BeFalse();
            registration.Command.Should().BeNull();
            var afterUpgrade = new UserAutostart(@"C:\Githubie\bin\Githubie.Server.exe", path, registration);
            afterUpgrade.Initialize().Should().BeFalse();
            afterUpgrade.IsEnabled().Should().BeFalse();
            File.ReadAllText(path).Should().Contain("false");
        });
    }

    [Fact]
    public void RegistryReadbackFailure_DoesNotCommitPreference()
    {
        WithPreference((path, registration) =>
        {
            registration.IgnoreWrites = true;
            var startup = new UserAutostart(@"C:\Githubie\bin\Githubie.Server.exe", path, registration);
            var initialize = () => startup.Initialize();
            initialize.Should().Throw<IOException>();
            File.Exists(path).Should().BeFalse();
            registration.Command.Should().BeNull();
        });
    }

    [Fact]
    public void ForeignStartupEntry_IsPreserved()
    {
        WithPreference((path, registration) =>
        {
            registration.Command = "foreign.exe";
            var startup = new UserAutostart(@"C:\Githubie\bin\Githubie.Server.exe", path, registration);
            var initialize = () => startup.Initialize();
            initialize.Should().Throw<IOException>();
            registration.Command.Should().Be("foreign.exe");
            File.Exists(path).Should().BeFalse();
        });
    }

    [Fact]
    public void PreferencePublicationFailure_RestoresPreviousStartupEntry()
    {
        WithPreference((path, registration) =>
        {
            Directory.CreateDirectory(path);
            var startup = new UserAutostart(@"C:\Githubie\bin\Githubie.Server.exe", path, registration);
            var change = () => startup.SetEnabled(true);
            change.Should().Throw<UnauthorizedAccessException>();
            registration.Command.Should().BeNull();
            Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Should().BeEmpty();
        });
    }

    private static void WithPreference(Action<string, RecordingRegistration> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "Githubie-startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(Path.Combine(root, "startup.json"), new RecordingRegistration()); }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class RecordingRegistration : IUserStartupRegistration
    {
        public string? Command { get; set; }
        public bool IgnoreWrites { get; set; }
        public string? Read() => Command;
        public void Write(string? command) { if (!IgnoreWrites) Command = command; }
    }
}
