using System.ComponentModel;
using FluentAssertions;
using ModelContextProtocol.Server;
using Xunit;

namespace Githubie.Server.Tests;

public sealed class GithubieMcpPromptsTests
{
    [Fact]
    public void ServerInstructions_ExplainsPurposeAndSafetyBoundary()
    {
        GithubieMcpPrompts.ServerInstructions.Should().Contain("policy-enforcing gateway");
        GithubieMcpPrompts.ServerInstructions.Should().Contain("github_repository_status");
        GithubieMcpPrompts.ServerInstructions.Should().Contain("call list_projects before github_push");
        GithubieMcpPrompts.ServerInstructions.Should().Contain("dry_run=true");
        GithubieMcpPrompts.ServerInstructions.Should().Contain("Never request or expose a Personal Access Token");
    }

    [Fact]
    public void GetUsageGuide_IsRegisteredAsGithubieUsagePrompt()
    {
        var method = typeof(GithubieMcpPrompts).GetMethod(nameof(GithubieMcpPrompts.GetUsageGuide));

        method.Should().NotBeNull();
        method!.GetCustomAttributes(typeof(McpServerPromptAttribute), false).Should().ContainSingle();
        method.GetCustomAttributes(typeof(DescriptionAttribute), false).Should().ContainSingle();
        GithubieMcpPrompts.GetUsageGuide().Should().Contain("Recommended workflow");
        GithubieMcpPrompts.GetUsageGuide().Should().Contain("call list_projects again immediately before github_push");
    }
}
