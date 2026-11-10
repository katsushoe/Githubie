using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Githubie.Cli.Tests;

public sealed class PrPublicationUserHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepositorySettingsFile_PreservesPublicationListAndExplicitClear(bool clear)
    {
        var path = Path.GetTempFileName();
        try
        {
            var payload = new { repository = "sample", changes = new { pr_publication_source_branches = clear ? Array.Empty<string>() : new[] { "docs/topic" } } };
            File.WriteAllText(path, JsonSerializer.Serialize(payload));
            var (method, parameters) = UserHostCli.ParseMcpCommand(["repo", "settings", "update", "--file", path]);
            method.Should().Be("tools/call");
            var request = JsonSerializer.SerializeToElement(parameters);
            request.GetProperty("name").GetString().Should().Be("github_repository_settings_update");
            request.GetProperty("arguments").ToString().Should().Be(JsonSerializer.SerializeToElement(payload).ToString());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RepositorySettingsGet_UsesReadOperationOnly()
    {
        var (_, parameters) = UserHostCli.ParseMcpCommand(["repo", "settings", "get", "sample"]);
        var request = JsonSerializer.SerializeToElement(parameters);
        request.GetProperty("name").GetString().Should().Be("github_repository_settings_get");
        request.GetProperty("arguments").EnumerateObject().Select(p => p.Name).Should().Equal("repository");
    }

    [Fact]
    public void PublishCreateFile_PreservesExplicitIdentityShaDraftAndText()
    {
        var path = Path.GetTempFileName();
        try
        {
            var payload = new { repository = "sample", source_branch = "work/topic", destination_branch = "review",
                expected_source_sha = new string('a', 40), operation_id = Guid.NewGuid().ToString("D"),
                title = "  explicit title  ", description = "body", draft = true };
            File.WriteAllText(path, JsonSerializer.Serialize(payload));
            var (method, parameters) = UserHostCli.ParseMcpCommand(["pr", "publish-create", "--file", path]);
            method.Should().Be("tools/call");
            var request = JsonSerializer.SerializeToElement(parameters);
            request.GetProperty("name").GetString().Should().Be("github_pr_publish_create");
            request.GetProperty("arguments").ToString().Should().Be(JsonSerializer.SerializeToElement(payload).ToString());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void PublishStatus_PreservesIdWithoutConstructingCreateArguments()
    {
        var id = Guid.NewGuid().ToString("D");
        var (method, parameters) = UserHostCli.ParseMcpCommand(["pr", "publish-status", "sample", id]);
        method.Should().Be("tools/call");
        var request = JsonSerializer.SerializeToElement(parameters);
        request.GetProperty("name").GetString().Should().Be("github_pr_publish_status");
        var arguments = request.GetProperty("arguments");
        arguments.EnumerateObject().Select(x => x.Name).Should().BeEquivalentTo("repository", "operation_id");
        arguments.GetProperty("operation_id").GetString().Should().Be(id);
    }
}
