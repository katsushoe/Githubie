using FluentAssertions;
using Githubie.Application.Git;
using Githubie.Infrastructure.Git;
using Xunit;

namespace Githubie.Infrastructure.Tests;

public sealed class GitCommandClientTests
{
    private sealed class RecordingProcessExecutor : IProcessExecutor
    {
        public IReadOnlyList<string>? CapturedArguments { get; private set; }

        public IReadOnlyDictionary<string, string>? CapturedEnvironment { get; private set; }

        public Task<GitCommandResult> ExecuteAsync(
            string workingDirectory,
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environmentOverrides,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            CapturedArguments = arguments;
            CapturedEnvironment = environmentOverrides;
            return Task.FromResult(GitCommandResult.Success(string.Empty));
        }
    }

    private const string RepositoryRoot = "C:\\repo";
    private const string AskPassPath = "C:\\install\\bin\\Githubie.AskPass.exe";

    [Fact]
    public async Task GetStatusAsync_UsesFixedLocalArguments_WithoutAskPassEnvironment()
    {
        var executor = new RecordingProcessExecutor();
        var client = new GitCommandClient(executor, AskPassPath);

        await client.GetStatusAsync(RepositoryRoot, CancellationToken.None);

        executor.CapturedArguments.Should().Equal("-c", $"safe.directory={RepositoryRoot}", "status", "--porcelain");
        executor.CapturedEnvironment.Should().NotContainKey(GitAskPassProtocol.AskPassVariable);
    }

    [Fact]
    public async Task PushAsync_UsesDoubleDashSeparatorAndAskPassEnvironment()
    {
        var executor = new RecordingProcessExecutor();
        var client = new GitCommandClient(executor, AskPassPath);

        await client.PushAsync(RepositoryRoot, "sample-repo", "origin", "develop", CancellationToken.None);

        executor.CapturedArguments.Should().Equal(
            "-c", $"safe.directory={RepositoryRoot}",
            "-c", "credential.helper=",
            "push", "--", "origin", "develop");

        executor.CapturedEnvironment.Should().ContainKey(GitAskPassProtocol.AskPassVariable)
            .WhoseValue.Should().Be(AskPassPath);
        executor.CapturedEnvironment.Should().Contain(GitAskPassProtocol.RepositoryIdVariable, "sample-repo");
        executor.CapturedEnvironment.Should().Contain(GitAskPassProtocol.AskPassRequireVariable, "force");
    }

    [Fact]
    public async Task PullFastForwardOnlyAsync_UsesFixedFlagAndDoubleDashSeparator()
    {
        var executor = new RecordingProcessExecutor();
        var client = new GitCommandClient(executor, AskPassPath);

        await client.PullFastForwardOnlyAsync(RepositoryRoot, "sample-repo", "origin", "main", CancellationToken.None);

        executor.CapturedArguments.Should().Equal(
            "-c", $"safe.directory={RepositoryRoot}",
            "-c", "credential.helper=",
            "pull", "--ff-only", "--", "origin", "main");
    }

    [Fact]
    public async Task GetRemoteUrlAsync_UsesDoubleDashSeparatorForRemoteName()
    {
        // remote名にAgent由来の値が渡ってもオプション注入されないことを確認する。
        var executor = new RecordingProcessExecutor();
        var client = new GitCommandClient(executor, AskPassPath);

        await client.GetRemoteUrlAsync(RepositoryRoot, "--upload-pack=evil", CancellationToken.None);

        executor.CapturedArguments.Should().Equal("-c", $"safe.directory={RepositoryRoot}", "remote", "get-url", "--", "--upload-pack=evil");
    }

    [Fact]
    public async Task PushHistoryRewriteAsync_UsesAtomicAndPerRefForceWithLease()
    {
        var executor = new RecordingProcessExecutor();
        var client = new GitCommandClient(executor, AskPassPath);
        var refs = new[]
        {
            new GitHistoryRewriteRef("refs/heads/main", new string('2', 40), new string('1', 40)),
            new GitHistoryRewriteRef("refs/tags/v1.0.0", new string('4', 40), new string('3', 40)),
        };

        await client.PushHistoryRewriteAsync(RepositoryRoot, "sample-repo", "origin", refs, CancellationToken.None);

        executor.CapturedArguments.Should().Equal(
            "-c", $"safe.directory={RepositoryRoot}", "-c", "credential.helper=", "push", "--atomic",
            $"--force-with-lease=refs/heads/main:{new string('1', 40)}",
            $"--force-with-lease=refs/tags/v1.0.0:{new string('3', 40)}",
            "--", "origin", $"{new string('2', 40)}:refs/heads/main", $"{new string('4', 40)}:refs/tags/v1.0.0");
    }
}
