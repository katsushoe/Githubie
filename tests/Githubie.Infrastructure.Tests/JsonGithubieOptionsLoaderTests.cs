using System.Text;
using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Infrastructure.Configuration;
using Xunit;

namespace Githubie.Infrastructure.Tests;

public sealed class JsonGithubieOptionsLoaderTests
{
    private const string ValidJson = """
        {
          "mcp_port": 45460,
          "mcp_path": "/mcp",
          "provider_authentication": {
            "issuer": "moyai:test",
            "trust_bundle_path": "C:\\Githubie\\config\\moyai-trust.json",
            "replay_database_path": "C:\\Githubie\\data\\moyai-replay.db",
            "projects": { "example": "11111111-1111-1111-1111-111111111111" }
          },
          "repositories": {
            "example": {
              "github_owner": "example-org",
              "github_repo": "example-repo",
              "local_root": "D:\\Projects\\Example",
              "remote": "origin",
              "develop_branch": "develop",
              "main_branch": "main",
              "direct_push_branches": ["develop"],
              "pull_branches": ["develop", "main"],
              "protected_branches": ["main"],
              "tag_target_branch": "main",
              "tag_pattern": "^v[0-9]+\\.[0-9]+\\.[0-9]+.*$",
              "merge_method": "merge",
              "require_clean_working_tree": true
            }
          }
        }
        """;

    [Fact]
    public async Task LoadAsync_ParsesValidConfiguration()
    {
        var loader = new JsonGithubieOptionsLoader();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ValidJson));

        var result = await loader.LoadAsync(stream, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Options!.McpPort.Should().Be(45460);
        result.Options.Repositories.Should().ContainKey("example");
        result.Options.Repositories["example"].GitHubOwner.Should().Be("example-org");
    }

    [Fact]
    public async Task LoadAsync_RejectsInvalidPort()
    {
        var json = ValidJson.Replace("\"mcp_port\": 45460", "\"mcp_port\": 0");
        var loader = new JsonGithubieOptionsLoader();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var result = await loader.LoadAsync(stream, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == ConfigurationErrorCode.InvalidMcpPort);
    }

    [Fact]
    public async Task LoadAsync_RejectsUnknownProperty()
    {
        var json = ValidJson.Replace("\"mcp_port\": 45460,", "\"mcp_port\": 45460, \"unknown_field\": true,");
        var loader = new JsonGithubieOptionsLoader();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var result = await loader.LoadAsync(stream, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_AcceptsMissingProviderAuthenticationForStandaloneUse()
    {
        var start = ValidJson.IndexOf("  \"provider_authentication\"", StringComparison.Ordinal);
        var end = ValidJson.IndexOf("  \"repositories\"", start, StringComparison.Ordinal);
        var json = ValidJson.Remove(start, end - start);
        var loader = new JsonGithubieOptionsLoader();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var result = await loader.LoadAsync(stream, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Options!.ProviderAuthentication.Should().BeNull();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("true", true)]
    public async Task LoadAsync_ReadsRequireAssertion(string? value, bool expected)
    {
        var json = value is null
            ? ValidJson
            : ValidJson.Replace("\"provider_authentication\": {", $"\"provider_authentication\": {{ \"require_assertion\": {value},");
        var loader = new JsonGithubieOptionsLoader();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var result = await loader.LoadAsync(stream, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Options!.ProviderAuthentication!.RequireAssertion.Should().Be(expected);
    }

    [Fact]
    public async Task LoadAsync_UnmappedRepository_IsRejectedOnlyInMoyaiIntegration()
    {
        var json = ValidJson.Replace(
            "\"projects\": { \"example\": \"11111111-1111-1111-1111-111111111111\" }",
            "\"projects\": { \"other\": \"11111111-1111-1111-1111-111111111111\" }");
        var loader = new JsonGithubieOptionsLoader();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var result = await loader.LoadAsync(stream, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        JsonGithubieOptionsLoader.ValidateMoyaiIntegration(result.Options!).Should().Contain(e =>
            e.Code == ConfigurationErrorCode.InvalidProviderAuthentication
            && e.Path == "$.provider_authentication.projects"
            && e.Message.Contains("example"));
    }

    [Fact]
    public async Task ValidateMoyaiIntegration_MissingProviderAuthentication_IsRejected()
    {
        var start = ValidJson.IndexOf("  \"provider_authentication\"", StringComparison.Ordinal);
        var end = ValidJson.IndexOf("  \"repositories\"", start, StringComparison.Ordinal);
        var loader = new JsonGithubieOptionsLoader();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ValidJson.Remove(start, end - start)));

        var result = await loader.LoadAsync(stream, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        JsonGithubieOptionsLoader.ValidateMoyaiIntegration(result.Options!).Should().ContainSingle(e =>
            e.Code == ConfigurationErrorCode.MissingProperty && e.Path == "$.provider_authentication");
    }

    [Theory]
    [InlineData("1example")]
    [InlineData("---")]
    [InlineData("example repo")]
    [InlineData("日本語")]
    public async Task LoadAsync_RejectsRepositoryIdOutsideProjectInboxRule(string repositoryId)
    {
        var json = ValidJson.Replace("\"example\": {", $"\"{repositoryId}\": {{");
        var loader = new JsonGithubieOptionsLoader();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var result = await loader.LoadAsync(stream, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == ConfigurationErrorCode.InvalidRepositoryId);
    }

    [Fact]
    public async Task LoadAsync_NormalizesLegacyRepositoryId()
    {
        var json = ValidJson
            .Replace("\"example\": {", "\"Example-Repo\": {")
            .Replace("\"projects\": { \"example\":", "\"projects\": { \"Example-Repo\":");
        var loader = new JsonGithubieOptionsLoader();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var result = await loader.LoadAsync(stream, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Options!.Repositories.Should().ContainKey("examplerepo");
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTrips()
    {
        var options = new GithubieOptions(
            McpPort: 45460,
            McpPath: "/mcp",
            Repositories: new Dictionary<string, RepositoryOptions>
            {
                ["example"] = new(
                    GitHubOwner: "example-org",
                    GitHubRepo: "example-repo",
                    LocalRoot: "D:\\Projects\\Example",
                    Remote: "origin",
                    DevelopBranch: "develop",
                    MainBranch: "main",
                    DirectPushBranches: ["develop"],
                    PullBranches: ["develop", "main"],
                    ProtectedBranches: ["main"],
                    TagTargetBranch: "main",
                    TagPattern: "^v[0-9]+\\.[0-9]+\\.[0-9]+.*$",
                    MergeMethod: "merge",
                    RequireCleanWorkingTree: true),
            })
        {
            ProviderAuthentication = new(
                "moyai:test",
                "C:\\Githubie\\config\\moyai-trust.json",
                "C:\\Githubie\\data\\moyai-replay.db",
                new Dictionary<string, Guid> { ["example"] = Guid.Parse("11111111-1111-1111-1111-111111111111") }),
        };

        var loader = new JsonGithubieOptionsLoader();
        await using var writeStream = new MemoryStream();
        await loader.SaveAsync(options, writeStream, TestContext.Current.CancellationToken);

        writeStream.Position = 0;
        var result = await loader.LoadAsync(writeStream, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Options.Should().BeEquivalentTo(options);
    }
}
