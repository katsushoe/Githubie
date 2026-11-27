using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.Credentials;
using Githubie.Application.GitHub;
using Githubie.Application.Interactive;
using Githubie.Application.Repositories;
using Githubie.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Githubie.Server.Tests;

public sealed class UserHostCredentialsTests
{
    [Theory]
    [InlineData(InteractiveTokenPromptOutcome.Cancelled, "CANCELLED")]
    [InlineData(InteractiveTokenPromptOutcome.Skipped, "CANCELLED")]
    [InlineData(InteractiveTokenPromptOutcome.LaunchFailed, "DIALOG_FAILED")]
    [InlineData(InteractiveTokenPromptOutcome.ProtocolError, "DIALOG_FAILED")]
    [InlineData(InteractiveTokenPromptOutcome.NoInteractiveSession, "DIALOG_FAILED")]
    public async Task TokenDialogFailure_PreservesExistingCredential(InteractiveTokenPromptOutcome outcome, string code)
    {
        var prompt = Substitute.For<IInteractiveTokenPrompt>();
        prompt.RequestTokenAsync(Arg.Any<TokenPromptRequest>(), Arg.Any<CancellationToken>())
            .Returns(InteractiveTokenPromptResult.Failure(outcome));
        var tokens = new TokenStore();
        var result = await Create(prompt, tokens).ExecuteAsync(new("not-returned", "auth_set", "sample"), TestContext.Current.CancellationToken);
        result.Success.Should().BeFalse();
        result.Code.Should().Be(code);
        tokens.Value.Should().Be("existing-secret");
        tokens.SaveCount.Should().Be(0);
        result.ToString().Should().NotContain("existing-secret").And.NotContain("not-returned");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TokenSave_ClearsInputAndPreservesOldCredentialOnFailure(bool succeeds)
    {
        var prompt = Substitute.For<IInteractiveTokenPrompt>();
        var input = "new-secret".ToCharArray();
        prompt.RequestTokenAsync(Arg.Any<TokenPromptRequest>(), Arg.Any<CancellationToken>())
            .Returns(InteractiveTokenPromptResult.Accepted(input));
        var tokens = new TokenStore { Succeeds = succeeds };
        var result = await Create(prompt, tokens).ExecuteAsync(new("not-returned", "auth_set", "sample"), TestContext.Current.CancellationToken);
        result.Success.Should().Be(succeeds);
        result.Code.Should().Be(succeeds ? "saved" : "SAVE_FAILED");
        tokens.Value.Should().Be(succeeds ? "new-secret" : "existing-secret");
        input.Should().OnlyContain(character => character == '\0');
        result.ToString().Should().NotContain("new-secret").And.NotContain("existing-secret");
        await prompt.Received(1).RequestTokenAsync(Arg.Is<TokenPromptRequest>(request => !request.AllowSkip &&
            request.ProjectName == "sample" && request.RepositoryUrl == "https://github.com/owner/repo"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnregisteredRepository_DoesNotOpenDialogOrChangeToken()
    {
        var prompt = Substitute.For<IInteractiveTokenPrompt>();
        var tokens = new TokenStore();
        var result = await Create(prompt, tokens).ExecuteAsync(new("not-returned", "auth_set", "missing"), TestContext.Current.CancellationToken);
        result.Code.Should().Be("repository_not_found");
        tokens.SaveCount.Should().Be(0);
        await prompt.DidNotReceiveWithAnyArgs().RequestTokenAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task OpenTokenDialog_SerializesRepositoryRemovalUntilTokenDecisionCompletes()
    {
        var mutations = new RepositoryMutationLock();
        var allowlist = new RepositoryAllowlist(new Dictionary<string, RepositoryOptions>
        { ["sample"] = new("owner", "repo", @"C:\sample", "origin", "develop", "main", ["develop"], ["develop"], ["main"], "main", "^v.*$", "merge", true) });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var decision = new TaskCompletionSource<InteractiveTokenPromptResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompt = Substitute.For<IInteractiveTokenPrompt>();
        prompt.RequestTokenAsync(Arg.Any<TokenPromptRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
        { entered.TrySetResult(); return decision.Task; });
        var order = new List<string>();
        var tokens = new TokenStore { Saved = () => order.Add("save") };
        var atomic = Substitute.For<IAtomicRepositoryManagementStore>();
        atomic.DeleteRegistrationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        { order.Add("delete"); return Task.CompletedTask; });
        var management = new RepositoryManagementService(allowlist, Substitute.For<IInteractiveApprovalPrompt>(),
            Substitute.For<IRepositoryConfigurationStore>(), tokens, atomicManagementStore: atomic, mutationLock: mutations);
        var controller = new UserHostCredentials(allowlist, prompt, tokens, Substitute.For<IGitHubRepositoryGateway>(),
            NullLogger<UserHostCredentials>.Instance, mutations);
        var save = controller.ExecuteAsync(new("not-returned", "auth_set", "sample"), TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var remove = management.UnregisterAsync("sample", TestContext.Current.CancellationToken);
        remove.IsCompleted.Should().BeFalse("removal cannot change the target of an open token dialog");
        decision.SetResult(InteractiveTokenPromptResult.Accepted("new-secret".ToCharArray()));
        (await save).Success.Should().BeTrue();
        (await remove).IsSuccess.Should().BeTrue();
        order.Should().Equal("save", "delete");
        allowlist.TryGet("sample", out _).Should().BeFalse();
    }

    private static UserHostCredentials Create(IInteractiveTokenPrompt prompt, TokenStore tokens) => new(new RepositoryAllowlist(
        new Dictionary<string, RepositoryOptions> { ["sample"] = new("owner", "repo", @"C:\sample", "origin", "develop", "main",
            ["develop"], ["develop", "main"], ["main"], "main", "^v.*$", "merge", true) }),
        prompt, tokens, Substitute.For<IGitHubRepositoryGateway>(), NullLogger<UserHostCredentials>.Instance);

    private sealed class TokenStore : IApiTokenStore
    {
        public string Value { get; private set; } = "existing-secret";
        public int SaveCount { get; private set; }
        public bool Succeeds { get; init; } = true;
        public Action? Saved { get; init; }
        public ApiTokenStoreResult Save(string repositoryId, ReadOnlySpan<char> token)
        {
            SaveCount++;
            if (!Succeeds) return ApiTokenStoreResult.Failure(ApiTokenStoreError.IoError);
            Value = new string(token);
            Saved?.Invoke();
            return ApiTokenStoreResult.Success();
        }
        public ApiTokenStoreReadResult Read(string repositoryId) => ApiTokenStoreReadResult.Success(Value.ToCharArray());
        public ApiTokenStoreResult Delete(string repositoryId) => ApiTokenStoreResult.Success();
        public ApiTokenStoreResult Rename(string oldId, string newId) => ApiTokenStoreResult.Success();
    }
}
