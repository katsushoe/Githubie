using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.GitHub;
using Githubie.Application.Repositories;
using NSubstitute;
using Xunit;

namespace Githubie.Application.Tests;

public sealed class GitHubWorkflowGatewayTests
{
    private readonly IGitHubApiClient _api = Substitute.For<IGitHubApiClient>();
    private readonly GitHubRepositoryGateway _gateway;

    public GitHubWorkflowGatewayTests()
    {
        var options = new RepositoryOptions(
            "owner", "repo", "C:\\repo", "origin", "develop", "main", ["develop"], ["develop", "main"], ["main"],
            "main", "^v", "merge", true)
        {
            Workflows = new Dictionary<string, WorkflowPolicyOptions>
            {
                ["release.yml"] = new(["main"], new Dictionary<string, WorkflowInputPolicyOptions>
                {
                    ["version"] = new("string", true, 20),
                    ["dry_run"] = new("boolean", false, 5),
                }, 1, 1),
            },
        };
        _gateway = new(new RepositoryAllowlist(new Dictionary<string, RepositoryOptions> { ["sample"] = options }), _api);
    }

    [Theory]
    [InlineData("unknown.yml", "main", "version", "1.0", GitHubError.WorkflowNotAllowed)]
    [InlineData("release.yml", "develop", "version", "1.0", GitHubError.WorkflowRefNotAllowed)]
    [InlineData("release.yml", "main", "unknown", "1.0", GitHubError.WorkflowInputInvalid)]
    [InlineData("release.yml", "main", "dry_run", "not-bool", GitHubError.WorkflowInputInvalid)]
    public async Task DispatchWorkflowAsync_RejectsValuesOutsidePolicy(
        string workflow, string reference, string key, string value, GitHubError expected)
    {
        var inputs = new Dictionary<string, string> { [key] = value };
        var result = await _gateway.DispatchWorkflowAsync(
            "sample", new(workflow, reference, inputs), TestContext.Current.CancellationToken);
        result.Error.Should().Be(expected);
        _api.ReceivedCalls().Select(call => call.GetMethodInfo().Name)
            .Should().NotContain(nameof(IGitHubApiClient.DispatchWorkflowAsync));
    }

    [Fact]
    public async Task DispatchWorkflowAsync_CorrelatesSingleNewRun()
    {
        var oldRun = Run(1, DateTimeOffset.UtcNow.AddMinutes(-1));
        var newRun = Run(2, DateTimeOffset.UtcNow);
        _api.ListWorkflowRunsAsync("sample", "owner", "repo", "release.yml", "main", "workflow_dispatch", null, 20, Arg.Any<CancellationToken>())
            .Returns(
                GitHubResult<IReadOnlyList<GitHubWorkflowRunInfo>>.Success([oldRun]),
                GitHubResult<IReadOnlyList<GitHubWorkflowRunInfo>>.Success([newRun, oldRun]));
        _api.DispatchWorkflowAsync("sample", "owner", "repo", Arg.Any<GitHubWorkflowDispatchRequest>(), Arg.Any<CancellationToken>())
            .Returns(GitHubResult<bool>.Success(true));

        var result = await _gateway.DispatchWorkflowAsync("sample",
            new("release.yml", "main", new Dictionary<string, string> { ["version"] = "1.0" }),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Run.Id.Should().Be(2);
    }

    [Fact]
    public async Task DispatchWorkflowAsync_RejectsAmbiguousNewRuns()
    {
        var first = Run(2, DateTimeOffset.UtcNow);
        var second = Run(3, DateTimeOffset.UtcNow);
        _api.ListWorkflowRunsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                GitHubResult<IReadOnlyList<GitHubWorkflowRunInfo>>.Success([]),
                GitHubResult<IReadOnlyList<GitHubWorkflowRunInfo>>.Success([first, second]));
        _api.DispatchWorkflowAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<GitHubWorkflowDispatchRequest>(), Arg.Any<CancellationToken>())
            .Returns(GitHubResult<bool>.Success(true));

        var result = await _gateway.DispatchWorkflowAsync("sample",
            new("release.yml", "main", new Dictionary<string, string> { ["version"] = "1.0" }),
            TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitHubError.WorkflowRunCorrelationFailed);
    }

    private static GitHubWorkflowRunInfo Run(long id, DateTimeOffset created) =>
        new(id, "Release", "main", "abc", "workflow_dispatch", "queued", null, "user", created, created, $"https://example/{id}");
}
