using System.Text.Json.Nodes;
using FluentAssertions;
using Tomlyn;
using Tomlyn.Model;
using Xunit;

namespace Githubie.Cli.Tests;

public sealed class ClientRegistrationTests
{
    private static ClientRegistration Registration => new(@"C:\Githubie\bin\githubie.exe");

    [Theory]
    [InlineData("codex", "model = 'keep-me'\n[mcp_servers.other]\ncommand = 'other.exe'\n")]
    [InlineData("claude", "{\"theme\":\"keep-me\",\"mcpServers\":{\"other\":{\"command\":\"other.exe\"}}}")]
    public void ConfigureThenUnconfigure_PreservesOtherSettingsAndIsIdempotent(string client, string original)
    {
        var configured = Registration.Edit(client, original, true, false)!;
        configured.Should().Contain("keep-me").And.Contain("other.exe").And.Contain("stdio");
        Registration.Edit(client, configured, true, false).Should().BeNull();
        var removed = Registration.Edit(client, configured, false, false)!;
        removed.Should().Contain("keep-me").And.Contain("other.exe").And.NotContain("githubie.exe");
        Registration.Edit(client, removed, false, false).Should().BeNull();
        if (client == "codex") TomlSerializer.Deserialize<TomlTable>(removed)!["model"].Should().Be("keep-me");
        else JsonNode.Parse(removed)!["theme"]!.GetValue<string>().Should().Be("keep-me");
    }

    [Theory]
    [InlineData("codex", "[mcp_servers.githubie]\ncommand='foreign.exe'\n")]
    [InlineData("claude", "{\"mcpServers\":{\"githubie\":{\"command\":\"foreign.exe\"}}}")]
    public void ForeignEntry_IsPreservedForConfigureAndUnconfigure(string client, string original)
    {
        var configure = () => Registration.Edit(client, original, true, true);
        configure.Should().Throw<IOException>();
        var remove = () => Registration.Edit(client, original, false, false);
        remove.Should().Throw<IOException>();
    }

    [Theory]
    [InlineData("codex", "[mcp_servers.githubie]\nurl='http://127.0.0.1:45460/mcp'\n")]
    [InlineData("claude", "{\"mcpServers\":{\"githubie\":{\"type\":\"http\",\"url\":\"http://127.0.0.1:45460/mcp\"}}}")]
    public void LegacyEndpoint_RequiresExplicitMigration(string client, string original)
    {
        var preserve = () => Registration.Edit(client, original, true, false);
        preserve.Should().Throw<IOException>();
        var migrated = Registration.Edit(client, original, true, true)!;
        migrated.Should().Contain("stdio").And.NotContain("45460");
    }

    [Theory]
    [InlineData("codex", "[mcp_servers.githubie]\nurl='http://127.0.0.1:45460/mcp'\n[mcp_servers.githubie.http_headers]\nAuthorization='do-not-discard'\n")]
    [InlineData("claude", "{\"mcpServers\":{\"githubie\":{\"type\":\"http\",\"url\":\"http://127.0.0.1:45460/mcp\",\"headers\":{\"Authorization\":\"do-not-discard\"}}}}")]
    public void LegacyWithCredentialConfiguration_IsNotOverwritten(string client, string original)
    {
        var replace = () => Registration.Edit(client, original, true, true);
        replace.Should().Throw<IOException>();
    }

    [Fact]
    public async Task InvalidConfiguration_LeavesFileBytesUnchanged()
    {
        var root = Path.Combine(Path.GetTempPath(), "Githubie-client-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "config.toml");
        try
        {
            await File.WriteAllTextAsync(path, "invalid = [", TestContext.Current.CancellationToken);
            var before = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            var configure = () => Registration.ApplyAsync("codex", path, true, false, TestContext.Current.CancellationToken);
            await configure.Should().ThrowAsync<InvalidDataException>();
            (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)).Should().Equal(before);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("codex", "config.toml")]
    [InlineData("claude", "claude.json")]
    public async Task AtomicFileUpdate_ReadsBackValidConfigurationAndRemovesOnlyOwnedEntry(string client, string fileName)
    {
        var root = Path.Combine(Path.GetTempPath(), "Githubie-client-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, fileName);
        try
        {
            (await Registration.ApplyAsync(client, path, true, false, TestContext.Current.CancellationToken)).Should().BeTrue();
            (await Registration.ApplyAsync(client, path, true, false, TestContext.Current.CancellationToken)).Should().BeFalse();
            (await Registration.ApplyAsync(client, path, false, false, TestContext.Current.CancellationToken)).Should().BeTrue();
            Directory.GetFiles(root, "*.tmp").Should().BeEmpty();
        }
        finally { Directory.Delete(root, true); }
    }
}
