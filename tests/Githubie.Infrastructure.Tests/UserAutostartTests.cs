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
            File.ReadAllText(path).Should().Be(DefaultConfiguration);
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
            File.ReadAllText(path).Should().Be(DefaultConfiguration);
        });
    }

    [Fact]
    public void PreferencePublicationFailure_RestoresPreviousStartupEntry()
    {
        WithPreference((path, registration) =>
        {
            File.Delete(path);
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
        var path = Path.Combine(root, "githubie.json");
        File.WriteAllText(path, DefaultConfiguration);
        try { action(path, new RecordingRegistration()); }
        finally { Directory.Delete(root, recursive: true); }
    }

    private const string DefaultConfiguration = "{\"mcp_port\":45460,\"mcp_path\":\"/mcp\",\"repositories\":{},\"provider_authentication\":{\"issuer\":\"preserve\"}}";

    [Fact]
    public void ReadOnlyCentralFile_RollsBackRegistryAndPreservesConfiguration()
    {
        WithPreference((path, registration) =>
        {
            var startup = new UserAutostart(@"C:\Githubie\bin\Githubie.Server.exe", path, registration);
            startup.Initialize().Should().BeTrue();
            var originalCommand = registration.Command;
            File.SetAttributes(path, FileAttributes.ReadOnly);
            try
            {
                var change = () => startup.SetEnabled(false);
                change.Should().Throw<UnauthorizedAccessException>();
                registration.Command.Should().Be(originalCommand);
                File.ReadAllText(path).Should().Be(DefaultConfiguration);
            }
            finally { File.SetAttributes(path, FileAttributes.Normal); }
        });
    }

    [Fact]
    public void Toggle_ChangesOnlyCentralAutostartAndPreservesHostModeAndOtherSettings()
    {
        WithPreference((path, registration) =>
        {
            File.WriteAllText(path, "{\"mcp_port\":12345,\"repositories\":{\"keep\":{\"remote\":\"origin\"}},\"provider_authentication\":{\"issuer\":\"keep\"},\"user_host\":{\"moyai_integration\":true,\"direct_unrestricted\":true,\"autostart_enabled\":true}}");
            var startup = new UserAutostart(@"C:\Githubie\bin\Githubie.Server.exe", path, registration);
            startup.SetEnabled(false).Should().BeFalse();
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            json.RootElement.GetProperty("mcp_port").GetInt32().Should().Be(12345);
            json.RootElement.GetProperty("repositories").GetProperty("keep").GetProperty("remote").GetString().Should().Be("origin");
            json.RootElement.GetProperty("provider_authentication").GetProperty("issuer").GetString().Should().Be("keep");
            var host = json.RootElement.GetProperty("user_host");
            host.GetProperty("moyai_integration").GetBoolean().Should().BeTrue();
            host.GetProperty("direct_unrestricted").GetBoolean().Should().BeTrue();
            host.GetProperty("autostart_enabled").GetBoolean().Should().BeFalse();
        });
    }

    [Fact]
    public void MissingCentralConfiguration_DoesNotCreateSettingsOrChangeRegistration()
    {
        WithPreference((path, registration) =>
        {
            File.Delete(path);
            var startup = new UserAutostart(@"C:\Githubie\bin\Githubie.Server.exe", path, registration);
            var initialize = () => startup.Initialize();
            initialize.Should().Throw<FileNotFoundException>();
            registration.Command.Should().BeNull();
            File.Exists(path).Should().BeFalse();
        });
    }

    [Fact]
    public void CustomHostExecutable_UsesItsInstallationConfigurationAndQuotedStartupCommand()
    {
        var root = Path.Combine(Path.GetTempPath(), "Githubie custom install " + Guid.NewGuid().ToString("N"));
        var config = Path.Combine(root, "config", "githubie.json");
        var executable = Path.Combine(root, "bin", "Githubie.Server.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        File.WriteAllText(config, DefaultConfiguration);
        try
        {
            var registration = new RecordingRegistration();
            var startup = new UserAutostart(executable, registration: registration);
            startup.Initialize().Should().BeTrue();
            registration.Command.Should().Be($"\"{executable}\" --user-host --autostart");
            startup.SetEnabled(false).Should().BeFalse();
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(config));
            json.RootElement.GetProperty("user_host").GetProperty("autostart_enabled").GetBoolean().Should().BeFalse();
            registration.Command.Should().BeNull();
        }
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
