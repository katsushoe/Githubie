using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.Git;
using Githubie.Application.GitHub;
using Githubie.Application.Publishing;
using Githubie.Application.Repositories;
using ModelContextProtocol.Server;
using NSubstitute;
using Xunit;

namespace Githubie.Server.Tests;

public sealed class PrPublicationContractTests
{
    private static GithubieMcpTools Tools(IPrPublicationService? service = null) => new(
        Substitute.For<IGitGateway>(), Substitute.For<IGitHubRepositoryGateway>(),
        Substitute.For<IRepositoryRegistrationService>(), Substitute.For<IRepositoryManagementService>(),
        new RepositoryAllowlist(new Dictionary<string, RepositoryOptions>
        {
            ["sample"] = new("owner", "repo", "C:\\fixture", "origin", "develop", "main", [], [], ["main"], "main", "^v", "merge", true),
        }), publicationService: service);

    [Fact]
    public void CreateSchema_RequiresFixedSourceRouteShaAndOperationId()
    {
        var method = typeof(GithubieMcpTools).GetMethod(nameof(GithubieMcpTools.PublishPullRequestAsync))!;
        var tool = McpServerTool.Create(method, Tools());
        tool.ProtocolTool.Name.Should().Be("github_pr_publish_create");
        tool.ProtocolTool.InputSchema.GetProperty("required").EnumerateArray().Select(x => x.GetString())
            .Should().BeEquivalentTo(["repository", "source_branch", "destination_branch", "expected_source_sha", "title", "operation_id"]);
        GithubieAssertionPolicy.TryGetScopes(tool.ProtocolTool.Name, out string[] scopes).Should().BeTrue();
        scopes.Should().BeEquivalentTo(["repository.push", "provider.githubie.pull_request.write"]);
    }

    [Fact]
    public void StatusSchema_IsReadScopeAndRequiresOperationId()
    {
        var method = typeof(GithubieMcpTools).GetMethod(nameof(GithubieMcpTools.GetPublicationStatusAsync))!;
        var tool = McpServerTool.Create(method, Tools());
        tool.ProtocolTool.InputSchema.GetProperty("required").EnumerateArray().Select(x => x.GetString())
            .Should().BeEquivalentTo(["repository", "operation_id"]);
        GithubieAssertionPolicy.TryGetScopes(tool.ProtocolTool.Name, out string[] scopes).Should().BeTrue();
        scopes.Should().Equal("repository.read");
    }

    [Fact]
    public async Task FailedCreate_PreservesPartialResultAndUnknownRecoveryContract()
    {
        var service = Substitute.For<IPrPublicationService>();
        string id = Guid.NewGuid().ToString("D");
        var info = new PrPublicationInfo(id, "work/topic", "review-target", new string('a', 40), "created", "unknown", Stage: "creating_pr");
        service.PublishAsync("sample", Arg.Any<PrPublishInput>(), Arg.Any<CancellationToken>())
            .Returns(new PrPublicationResult(info, "result_unknown", "unknown"));
        var result = await Tools(service).PublishPullRequestAsync("sample", "work/topic", "review-target", info.SourceSha, "Title", id,
            cancellationToken: TestContext.Current.CancellationToken);
        result.Ok.Should().BeFalse();
        result.Data.Should().Be(info);
        result.Error!.CommonCode.Should().Be(GithubieCommonErrorCode.ResultUnknown);
        result.Error.Outcome.Should().Be("unknown");
        result.Error.Retryable.Should().BeFalse();
        result.Error.SuggestedAction.Should().Be("check_status");
        result.Error.CorrelationId.Should().Be(Guid.Parse(id).ToString("N"));
    }

    [Fact]
    public async Task Status_DelegatesOnlyToReadOperation()
    {
        var service = Substitute.For<IPrPublicationService>();
        string id = Guid.NewGuid().ToString("D");
        var info = new PrPublicationInfo(id, "work/topic", "review-target", new string('a', 40), "already_present", "already_present");
        service.GetStatusAsync("sample", id, Arg.Any<CancellationToken>()).Returns(new PrPublicationResult(info));
        (await Tools(service).GetPublicationStatusAsync("sample", id, TestContext.Current.CancellationToken)).Data.Should().Be(info);
        await service.DidNotReceive().PublishAsync(Arg.Any<string>(), Arg.Any<PrPublishInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnconfiguredService_ReturnsCapabilityMissing()
    {
        var result = await Tools().GetPublicationStatusAsync("sample", Guid.NewGuid().ToString("D"), TestContext.Current.CancellationToken);
        result.Ok.Should().BeFalse();
        result.Error!.Code.Should().Be("provider_capability_missing");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Capability_AdvertisesOnlyConfiguredService(bool configured)
    {
        var tools = Tools(configured ? Substitute.For<IPrPublicationService>() : null);
        var result = await tools.GetProviderCapabilitiesAsync("sample", TestContext.Current.CancellationToken);
        result.Data!.PullRequestPublishCreate.Should().Be(configured);
        result.Data.PullRequestPublishStatus.Should().Be(configured);
    }

    [Fact]
    public async Task Audit_RecordsEffectsAndCorrelationWithoutTitleOrDescription()
    {
        var inner = Substitute.For<IPrPublicationService>();
        var audit = Substitute.For<IGithubieAuditLogger>();
        string id = Guid.NewGuid().ToString("D");
        var input = new PrPublishInput("work/topic", "review-target", new string('a', 40), "private title", "secret description", false, id);
        var expected = new PrPublicationResult(new(id, input.SourceBranch, input.DestinationBranch, input.ExpectedSourceSha, "created", "unknown"), "result_unknown", "unknown");
        inner.PublishAsync("sample", input, Arg.Any<CancellationToken>()).Returns(expected);
        var service = new AuditedPrPublicationService(inner, audit);
        (await service.PublishAsync("sample", input, TestContext.Current.CancellationToken)).Should().Be(expected);
        audit.Received(1).Write(Arg.Is<GithubieAuditEvent>(e => e.Tool == "github_pr_publish_create"
            && e.CorrelationId == Guid.Parse(id).ToString("N") && e.Result == "unknown"
            && e.Diagnostic != null && e.Diagnostic.Contains("created") && e.Diagnostic.Contains("unknown")
            && !e.Diagnostic.Contains(input.Title) && !e.Diagnostic.Contains(input.Description!)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuditFailure_PreservesPayloadWithoutRetry(bool success)
    {
        var inner = Substitute.For<IPrPublicationService>();
        var audit = Substitute.For<IGithubieAuditLogger>();
        string id = Guid.NewGuid().ToString("D");
        var info = new PrPublicationInfo(id, "work/topic", "review-target", new string('a', 40), "created", success ? "created" : "unknown");
        var expected = new PrPublicationResult(info, success ? null : "result_unknown", success ? "failed" : "unknown");
        inner.GetStatusAsync("sample", id, Arg.Any<CancellationToken>()).Returns(expected);
        audit.When(x => x.Write(Arg.Any<GithubieAuditEvent>())).Do(_ => throw new IOException("audit unavailable"));
        var result = await new AuditedPrPublicationService(inner, audit).GetStatusAsync("sample", id, TestContext.Current.CancellationToken);
        result.Data.Should().Be(info);
        result.ErrorCode.Should().Be(success ? "audit_unavailable" : "result_unknown");
        await inner.DidNotReceive().PublishAsync(Arg.Any<string>(), Arg.Any<PrPublishInput>(), Arg.Any<CancellationToken>());
    }
}
