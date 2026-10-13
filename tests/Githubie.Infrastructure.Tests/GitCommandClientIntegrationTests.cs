using System.Diagnostics;
using FluentAssertions;
using Githubie.Infrastructure.Git;
using Xunit;

namespace Githubie.Infrastructure.Tests;

public sealed class GitCommandClientIntegrationTests
{
    [Fact]
    public async Task SwitchBranchAsync_ExistingLocalBranch_ChangesCurrentBranch()
    {
        using var repository = await TemporaryGitRepository.CreateEmptyAsync();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(repository.Root, "file.txt"), "base", cancellationToken);
        await TemporaryGitRepository.RunGitForOutputAsync(repository.Root, "add", "--", "file.txt");
        await TemporaryGitRepository.RunGitForOutputAsync(repository.Root, "-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "-m", "base");
        await TemporaryGitRepository.RunGitForOutputAsync(repository.Root, "branch", "feature/test");
        var client = new GitCommandClient(new ProcessExecutor(), "unused-askpass.exe");

        var switched = await client.SwitchBranchAsync(repository.Root, "feature/test", cancellationToken);
        var current = await client.GetCurrentBranchAsync(repository.Root, cancellationToken);

        switched.IsSuccess.Should().BeTrue(switched.StandardError);
        current.IsSuccess.Should().BeTrue(current.StandardError);
        current.StandardOutput.Should().Be("feature/test");
    }

    [Fact]
    public async Task CommitSelectedAsync_CommitsPatchAndPreservesUnselectedWorkingTreeEdit()
    {
        using var repository = await TemporaryGitRepository.CreateEmptyAsync();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string file = Path.Combine(repository.Root, "notes.txt");
        await File.WriteAllTextAsync(file, "first\nsecond\n", cancellationToken);
        await TemporaryGitRepository.RunGitForOutputAsync(repository.Root, "add", "--", "notes.txt");
        await TemporaryGitRepository.RunGitForOutputAsync(repository.Root, "-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "-m", "base");
        var client = new GitCommandClient(new ProcessExecutor(), "unused-askpass.exe");
        string expectedHead = (await client.GetHeadAsync(repository.Root, cancellationToken)).StandardOutput.Trim();
        await File.WriteAllTextAsync(file, "selected\nother\n", cancellationToken);
        string patch = "diff --git a/notes.txt b/notes.txt\n--- a/notes.txt\n+++ b/notes.txt\n@@ -1,2 +1,2 @@\n-first\n+selected\n second\n";

        var commit = await client.CommitSelectedAsync(repository.Root, "selected hunk", "Test", "test@example.com", expectedHead, patch, cancellationToken);

        commit.IsSuccess.Should().BeTrue(commit.StandardError);
        (await TemporaryGitRepository.RunGitForOutputAsync(repository.Root, "show", "HEAD:notes.txt")).Should().Be("selected\nsecond");
        (await File.ReadAllTextAsync(file, cancellationToken)).Should().Be("selected\nother\n");
        (await client.GetStatusAsync(repository.Root, cancellationToken)).StandardOutput.Should().Contain("notes.txt");
    }

    [Fact]
    public async Task CommitSelectedAsync_UnbornBranchUsesEmptyExpectedHead()
    {
        using var repository = await TemporaryGitRepository.CreateEmptyAsync();
        var client = new GitCommandClient(new ProcessExecutor(), "unused-askpass.exe");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string patch = "diff --git a/initial.txt b/initial.txt\nnew file mode 100644\n--- /dev/null\n+++ b/initial.txt\n@@ -0,0 +1 @@\n+initial\n";

        var commit = await client.CommitSelectedAsync(repository.Root, "initial selected commit", "Test", "test@example.com", string.Empty, patch, cancellationToken);

        commit.IsSuccess.Should().BeTrue(commit.StandardError);
        (await TemporaryGitRepository.RunGitForOutputAsync(repository.Root, "show", "HEAD:initial.txt")).Should().Be("initial");
    }

    [Fact]
    public async Task SelectiveStash_PreservesUnselectedTrackedAndUntrackedChanges()
    {
        using var repository = await TemporaryGitRepository.CreateEmptyAsync();
        var client = new GitCommandClient(new ProcessExecutor(), "unused-askpass.exe");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(repository.Root, "a.txt"), "base a", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repository.Root, "b.txt"), "base b", cancellationToken);
        await TemporaryGitRepository.RunGitForOutputAsync(repository.Root, "add", "--", "a.txt", "b.txt");
        await TemporaryGitRepository.RunGitForOutputAsync(repository.Root, "-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "-m", "base");
        await File.WriteAllTextAsync(Path.Combine(repository.Root, "a.txt"), "changed a", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repository.Root, "b.txt"), "changed b", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repository.Root, "c.txt"), "new c", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repository.Root, "d.txt"), "new d", cancellationToken);

        var pushed = await client.PushStashAsync(repository.Root, "selected files", ["a.txt", "c.txt"], cancellationToken);

        pushed.IsSuccess.Should().BeTrue(pushed.StandardError);
        (await File.ReadAllTextAsync(Path.Combine(repository.Root, "a.txt"), cancellationToken)).Should().Be("base a");
        File.Exists(Path.Combine(repository.Root, "c.txt")).Should().BeFalse();
        (await File.ReadAllTextAsync(Path.Combine(repository.Root, "b.txt"), cancellationToken)).Should().Be("changed b");
        File.Exists(Path.Combine(repository.Root, "d.txt")).Should().BeTrue();
        var listed = await client.ListStashesAsync(repository.Root, cancellationToken);
        listed.IsSuccess.Should().BeTrue(listed.StandardError);
        var stashId = listed.StandardOutput.Split('\u001f', 2)[0];
        stashId.Should().HaveLength(40);
        var applied = await client.ApplyStashAsync(repository.Root, stashId, cancellationToken);
        applied.IsSuccess.Should().BeTrue(applied.StandardError);
        (await File.ReadAllTextAsync(Path.Combine(repository.Root, "a.txt"), cancellationToken)).Should().Be("changed a");
        (await File.ReadAllTextAsync(Path.Combine(repository.Root, "c.txt"), cancellationToken)).Should().Be("new c");
        (await File.ReadAllTextAsync(Path.Combine(repository.Root, "b.txt"), cancellationToken)).Should().Be("changed b");
    }

    [Fact]
    public async Task CommitAsync_NoRepositoryIdentity_UsesExplicitAuthor()
    {
        using var repository = await TemporaryGitRepository.CreateEmptyAsync();
        await File.WriteAllTextAsync(Path.Combine(repository.Root, "first.txt"), "first commit", TestContext.Current.CancellationToken);
        var client = new GitCommandClient(new ProcessExecutor(), "unused-askpass.exe");

        var add = await client.AddAllAsync(repository.Root, TestContext.Current.CancellationToken);
        var commit = await client.CommitAsync(repository.Root, "first", "Registered Writer", "writer@example.com",
            TestContext.Current.CancellationToken);

        add.IsSuccess.Should().BeTrue(add.StandardError);
        commit.IsSuccess.Should().BeTrue(commit.StandardError);
        var author = await TemporaryGitRepository.RunGitForOutputAsync(repository.Root, "log", "-1", "--format=%an <%ae>");
        author.Should().Be("Registered Writer <writer@example.com>");
    }

    [Fact]
    public async Task ListRemoteUrlsAsync_ReturnsConfiguredRemotesOrEmptyWhenNone()
    {
        using var repository = await TemporaryGitRepository.CreateEmptyAsync();
        var client = new GitCommandClient(new ProcessExecutor(), "unused-askpass.exe");

        var none = await client.ListRemoteUrlsAsync(repository.Root, TestContext.Current.CancellationToken);
        await TemporaryGitRepository.RunGitForOutputAsync(repository.Root, "remote", "add", "github-origin-https", "https://github.com/owner/repo.git");
        var listed = await client.ListRemoteUrlsAsync(repository.Root, TestContext.Current.CancellationToken);

        none.IsSuccess.Should().BeTrue(none.StandardError);
        none.StandardOutput.Should().BeEmpty();
        listed.IsSuccess.Should().BeTrue(listed.StandardError);
        listed.StandardOutput.Trim().Should().Be("remote.github-origin-https.url https://github.com/owner/repo.git");
    }

    [Fact]
    public async Task GetBranchAndHeadAsync_EmptyRepository_ReturnsUnbornBranchAndEmptyHead()
    {
        using var repository = await TemporaryGitRepository.CreateEmptyAsync();
        var client = new GitCommandClient(new ProcessExecutor(), "unused-askpass.exe");

        var branch = await client.GetCurrentBranchAsync(repository.Root, TestContext.Current.CancellationToken);
        var head = await client.GetHeadAsync(repository.Root, TestContext.Current.CancellationToken);

        branch.IsSuccess.Should().BeTrue(branch.StandardError);
        branch.StandardOutput.Should().Be("develop");
        head.IsSuccess.Should().BeTrue(head.StandardError);
        head.StandardOutput.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDiffAsync_UnbornRepository_ReturnsStagedChangesAgainstEmptyTree()
    {
        using var repository = await TemporaryGitRepository.CreateEmptyAsync();
        await File.WriteAllTextAsync(
            Path.Combine(repository.Root, "first.txt"),
            "first line",
            TestContext.Current.CancellationToken);
        await TemporaryGitRepository.RunGitForOutputAsync(repository.Root, "add", "first.txt");
        var client = new GitCommandClient(new ProcessExecutor(), "unused-askpass.exe");

        var result = await client.GetDiffAsync(repository.Root, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(result.StandardError);
        result.StandardOutput.Should().Contain("new file mode");
        result.StandardOutput.Should().Contain("+first line");
    }

    [Theory]
    [InlineData("git@github.com:katsushoe/Shiori.git")]
    [InlineData("https://github.com/katsushoe/Shiori.git")]
    public async Task GetRemoteUrlAsync_RealGit_ReturnsConfiguredGitHubUrl(string remoteUrl)
    {
        using var repository = await TemporaryGitRepository.CreateAsync(remoteUrl);
        var client = new GitCommandClient(new ProcessExecutor(), "unused-askpass.exe");

        var result = await client.GetRemoteUrlAsync(repository.Root, "origin", TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(result.StandardError);
        result.StandardOutput.Should().Be(remoteUrl);
    }

    [Fact]
    public async Task GetRemoteUrlAsync_RealGit_MissingRemoteReturnsFailedResult()
    {
        using var repository = await TemporaryGitRepository.CreateAsync("https://github.com/katsushoe/Shiori.git");
        var client = new GitCommandClient(new ProcessExecutor(), "unused-askpass.exe");

        var result = await client.GetRemoteUrlAsync(repository.Root, "missing", TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.StandardError.Should().Contain("missing");
    }

    [Fact]
    public async Task PushAsync_RealGit_InitialPushCreatesRemoteBranchAndLeavesNoAheadCommits()
    {
        using var repository = await TemporaryGitRepository.CreateWithBareRemoteAsync();
        var client = new GitCommandClient(new ProcessExecutor(), "unused-askpass.exe");

        var before = await client.GetRemoteRefAsync(
            repository.Root, "sample", "origin", "refs/heads/develop", TestContext.Current.CancellationToken);
        var push = await client.PushAsync(repository.Root, "sample", "origin", "develop", TestContext.Current.CancellationToken);
        var aheadBehind = await client.GetAheadBehindAsync(repository.Root, "origin", "develop", TestContext.Current.CancellationToken);

        before.IsSuccess.Should().BeTrue(before.StandardError);
        before.StandardOutput.Should().BeEmpty();
        push.IsSuccess.Should().BeTrue(push.StandardError);
        aheadBehind.IsSuccess.Should().BeTrue(aheadBehind.StandardError);
        aheadBehind.StandardOutput.Should().Be("0\t0");
        (await TemporaryGitRepository.RunGitForOutputAsync(repository.RemoteRoot, "rev-parse", "refs/heads/develop"))
            .Should().Be(await TemporaryGitRepository.RunGitForOutputAsync(repository.Root, "rev-parse", "HEAD"));
    }

    [Fact]
    public async Task PushAsync_RealGit_FastForwardUpdatesExistingRemoteBranch()
    {
        using var repository = await TemporaryGitRepository.CreateWithBareRemoteAsync();
        var client = new GitCommandClient(new ProcessExecutor(), "unused-askpass.exe");
        (await client.PushAsync(repository.Root, "sample", "origin", "develop", TestContext.Current.CancellationToken))
            .IsSuccess.Should().BeTrue();
        await repository.CommitAsync("second commit");

        var push = await client.PushAsync(repository.Root, "sample", "origin", "develop", TestContext.Current.CancellationToken);
        var aheadBehind = await client.GetAheadBehindAsync(
            repository.Root, "origin", "develop", TestContext.Current.CancellationToken);

        push.IsSuccess.Should().BeTrue(push.StandardError);
        aheadBehind.StandardOutput.Should().Be("0\t0");
        (await TemporaryGitRepository.RunGitForOutputAsync(repository.RemoteRoot, "rev-parse", "refs/heads/develop"))
            .Should().Be(await TemporaryGitRepository.RunGitForOutputAsync(repository.Root, "rev-parse", "HEAD"));
    }

    [Fact]
    public async Task PushAsync_RealGit_NonFastForwardReturnsRejectedError()
    {
        using var repository = await TemporaryGitRepository.CreateWithBareRemoteAsync();
        var client = new GitCommandClient(new ProcessExecutor(), "unused-askpass.exe");
        (await client.PushAsync(repository.Root, "sample", "origin", "develop", TestContext.Current.CancellationToken)).IsSuccess.Should().BeTrue();
        await repository.AdvanceRemoteAsync();
        await repository.CommitAsync("local change");

        var result = await client.PushAsync(repository.Root, "sample", "origin", "develop", TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.StandardError.Should().Contain("rejected");
    }

    private sealed class TemporaryGitRepository : IDisposable
    {
        private TemporaryGitRepository(string root, string? container = null, string? remoteRoot = null)
        {
            Root = root;
            Container = container ?? root;
            RemoteRoot = remoteRoot ?? string.Empty;
        }

        public string Root { get; }
        public string Container { get; }
        public string RemoteRoot { get; }

        public static async Task<TemporaryGitRepository> CreateAsync(string remoteUrl)
        {
            var root = Directory.CreateTempSubdirectory("githubie-git-").FullName;
            await RunGitAsync(root, "init");
            await RunGitAsync(root, "remote", "add", "origin", remoteUrl);
            return new TemporaryGitRepository(root);
        }

        public static async Task<TemporaryGitRepository> CreateEmptyAsync()
        {
            var root = Directory.CreateTempSubdirectory("githubie-empty-").FullName;
            await RunGitAsync(root, "init", "--initial-branch=develop");
            return new TemporaryGitRepository(root);
        }

        public static async Task<TemporaryGitRepository> CreateWithBareRemoteAsync()
        {
            var container = Directory.CreateTempSubdirectory("githubie-push-").FullName;
            var remote = Path.Combine(container, "remote.git");
            var local = Path.Combine(container, "local");
            Directory.CreateDirectory(remote);
            Directory.CreateDirectory(local);
            await RunGitAsync(remote, "init", "--bare");
            await RunGitAsync(local, "init");
            await RunGitAsync(local, "config", "user.name", "Githubie Test");
            await RunGitAsync(local, "config", "user.email", "githubie@example.invalid");
            await File.WriteAllTextAsync(Path.Combine(local, "file.txt"), "initial");
            await RunGitAsync(local, "add", "file.txt");
            await RunGitAsync(local, "commit", "-m", "initial");
            await RunGitAsync(local, "branch", "-M", "develop");
            await RunGitAsync(local, "remote", "add", "origin", remote);
            return new TemporaryGitRepository(local, container, remote);
        }

        public async Task CommitAsync(string content)
        {
            await File.AppendAllTextAsync(Path.Combine(Root, "file.txt"), content);
            await RunGitAsync(Root, "add", "file.txt");
            await RunGitAsync(Root, "commit", "-m", content);
        }

        public async Task AdvanceRemoteAsync()
        {
            var other = Path.Combine(Container, "other");
            await RunGitAsync(Container, "clone", "--branch", "develop", RemoteRoot, other);
            await RunGitAsync(other, "config", "user.name", "Githubie Test");
            await RunGitAsync(other, "config", "user.email", "githubie@example.invalid");
            await File.AppendAllTextAsync(Path.Combine(other, "file.txt"), "remote change");
            await RunGitAsync(other, "add", "file.txt");
            await RunGitAsync(other, "commit", "-m", "remote change");
            await RunGitAsync(other, "push", "origin", "develop");
        }

        public void Dispose()
        {
            foreach (var file in Directory.EnumerateFiles(Container, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Container, recursive: true);
        }

        public static async Task<string> RunGitForOutputAsync(string root, params string[] arguments)
        {
            var (output, _) = await RunGitCoreAsync(root, arguments);
            return output;
        }

        private static async Task RunGitAsync(string root, params string[] arguments)
        {
            await RunGitCoreAsync(root, arguments);
        }

        private static async Task<(string Output, string Error)> RunGitCoreAsync(string root, params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git.");
            var standardOutput = await process.StandardOutput.ReadToEndAsync();
            var standardError = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"git failed: {standardError}");
            return (standardOutput.Trim(), standardError.Trim());
        }
    }
}
