using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Githubie.Infrastructure.Configuration;
using Xunit;

namespace Githubie.Infrastructure.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class UserHostConnectionStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Githubie-discovery-" + Guid.NewGuid().ToString("N"));
    private readonly string _data;
    private readonly UserProfileIdentity _identity;
    private readonly UserHostConnectionStore _store;
    private string RecordPath => Path.Combine(_data, "connection.json");

    public UserHostConnectionStoreTests()
    {
        using var user = WindowsIdentity.GetCurrent();
        var sid = user.User!;
        _data = Path.Combine(_root, "data");
        Directory.CreateDirectory(_data);
        foreach (var path in new[] { _root, _data })
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(security);
        }
        _identity = new(1, Guid.NewGuid(), sid.Value);
        File.WriteAllText(Path.Combine(_root, "profile.json"), JsonSerializer.Serialize(_identity));
        _store = new(_data, Environment.ProcessPath);
    }

    [Fact]
    public async Task VersionedRecord_RoundTripsAndNeverPublishesPlaintextCredential()
    {
        var connection = await Publish();
        var json = File.ReadAllText(RecordPath);
        var record = JsonNode.Parse(json)!;
        record["schema_version"]!.GetValue<int>().Should().Be(1);
        record["provider_id"]!.GetValue<string>().Should().Be("githubie");
        record["runtime_sha256"]!.GetValue<string>().Should().HaveLength(64);
        record["profile_sha256"]!.GetValue<string>().Should().HaveLength(64);
        json.Should().NotContain(connection.Credential).And.NotContain("\"credential\"");
        _store.Read(_identity).Should().Be(connection);
        connection.ToString().Should().NotContain(connection.Credential);
        Directory.GetFiles(_data, "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData("schema_version", "2", "discovery_unsupported")]
    [InlineData("provider_id", "\"buckettie\"", "discovery_unsupported")]
    [InlineData("owner_sid", "\"S-1-0-0\"", "discovery_profile_mismatch")]
    [InlineData("profile_id", "\"00000000-0000-0000-0000-000000000000\"", "discovery_profile_mismatch")]
    [InlineData("session_id", "2147483647", "discovery_profile_mismatch")]
    [InlineData("generation", "\"old\"", "discovery_profile_mismatch")]
    [InlineData("profile_sha256", "\"changed\"", "discovery_profile_mismatch")]
    [InlineData("runtime_sha256", "\"changed\"", "discovery_runtime_mismatch")]
    [InlineData("process_id", "2147483647", "discovery_host_stale")]
    [InlineData("process_start_utc_ticks", "1", "discovery_host_stale")]
    [InlineData("endpoint", "\"http://127.0.0.1:45460/other\"", "discovery_endpoint_rejected")]
    [InlineData("endpoint", "\"http://example.com:45460/mcp\"", "discovery_endpoint_rejected")]
    [InlineData("endpoint", "\"http://127.0.0.1:45460/mcp?credential=never\"", "discovery_endpoint_rejected")]
    [InlineData("protected_credential", "\"not-base64\"", "discovery_invalid")]
    [InlineData("protected_credential", "\"AAAA\"", "discovery_credential_invalid")]
    [InlineData("runtime_path", "null", "discovery_invalid")]
    public async Task TamperedRecord_IsRejectedBeforeUse(string field, string replacement, string code)
    {
        var connection = await Publish();
        var record = JsonNode.Parse(File.ReadAllText(RecordPath))!;
        record[field] = JsonNode.Parse(replacement);
        File.WriteAllText(RecordPath, record.ToJsonString());
        var read = () => _store.Read(_identity);
        var error = read.Should().Throw<UserHostDiscoveryException>().Which;
        error.Code.Should().Be(code);
        error.ToString().Should().NotContain(connection.Credential);
    }

    [Theory]
    [InlineData("{\"schema_version\":1,\"schema_version\":1}")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not-json")]
    public async Task InvalidEnvelope_HasStableRedactedError(string json)
    {
        await Publish();
        File.WriteAllText(RecordPath, json);
        var read = () => _store.Read(_identity);
        read.Should().Throw<UserHostDiscoveryException>().Which.Code.Should().Be("discovery_invalid");
    }

    [Fact]
    public async Task SelectedRuntimeAndProfileFile_ArePinned()
    {
        await Publish();
        var wrongRuntime = new UserHostConnectionStore(_data, Path.Combine(_root, "foreign.exe"));
        var readWrong = () => wrongRuntime.Read(_identity);
        readWrong.Should().Throw<UserHostDiscoveryException>().Which.Code.Should().Be("discovery_runtime_mismatch");
        File.AppendAllText(Path.Combine(_root, "profile.json"), " ");
        var readChanged = () => _store.Read(_identity);
        readChanged.Should().Throw<UserHostDiscoveryException>().Which.Code.Should().Be("discovery_profile_mismatch");
    }

    [Fact]
    public async Task AclBroadenedAfterPublish_IsRejectedOnNextRead()
    {
        await Publish();
        var info = new FileInfo(RecordPath);
        var security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Read, AccessControlType.Allow));
        info.SetAccessControl(security);
        var read = () => _store.Read(_identity);
        read.Should().Throw<UserHostDiscoveryException>().Which.Code.Should().Be("discovery_acl_rejected");
    }

    [Fact]
    public async Task Rotation_OldGenerationCannotRevokeSuccessor()
    {
        var old = await Publish();
        var current = await Publish();
        current.Credential.Should().NotBe(old.Credential);
        current.Generation.Should().NotBe(old.Generation);
        _store.Revoke(old);
        _store.Read(_identity).Should().Be(current);
        _store.Revoke(current);
        File.Exists(RecordPath).Should().BeFalse();
    }

    [Fact]
    public void LegacyDiscovery_IsNeverAdopted()
    {
        File.WriteAllBytes(Path.Combine(_data, "connection.dpapi"), [1, 2, 3]);
        var read = () => _store.Read(_identity);
        read.Should().Throw<UserHostDiscoveryException>().Which.Code.Should().Be("discovery_missing");
    }

    [Fact]
    public async Task DirectoryJunction_IsRejected()
    {
        await Publish();
        var alias = Path.Combine(_root, "linked-data");
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "/d", "/c", "mklink", "/J", alias, _data }) start.ArgumentList.Add(arg);
        using var command = Process.Start(start)!;
        await command.WaitForExitAsync(TestContext.Current.CancellationToken);
        command.ExitCode.Should().Be(0);
        try
        {
            var linked = new UserHostConnectionStore(alias, Environment.ProcessPath);
            var read = () => linked.Read(_identity);
            read.Should().Throw<UserHostDiscoveryException>().Which.Code.Should().Be("discovery_reparse_rejected");
        }
        finally { Directory.Delete(alias); }
    }

    private Task<UserHostConnection> Publish() => _store.PublishAsync(_identity,
        new Uri("http://127.0.0.1:45460/mcp"), TestContext.Current.CancellationToken);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
