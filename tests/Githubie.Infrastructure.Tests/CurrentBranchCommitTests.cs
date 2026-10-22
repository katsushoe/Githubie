using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.Git;
using Githubie.Application.Interactive;
using Githubie.Application.Repositories;
using Githubie.Infrastructure.Git;
using Githubie.Infrastructure.Repositories;
using Xunit;

namespace Githubie.Infrastructure.Tests;

public sealed class CurrentBranchCommitTests
{
    private const string Patch = "diff --git a/file.txt b/file.txt\n--- a/file.txt\n+++ b/file.txt\n@@ -1 +1 @@\n-base\n+selected\n";

    [Fact]
    public async Task SelectedCommit_ExistingStaging_LeavesStagedAndWorkingContentUnchanged()
    {
        using var repository = await Repository.CreateAsync("develop");
        await File.WriteAllTextAsync(repository.File, "staged\n", TestContext.Current.CancellationToken);
        await repository.Git("add", "--", "file.txt");
        await File.WriteAllTextAsync(repository.File, "unstaged\n", TestContext.Current.CancellationToken);
        var index = await File.ReadAllBytesAsync(repository.Index, TestContext.Current.CancellationToken);
        var result = await new GitCommandClient(new ProcessExecutor(), "unused.exe").CommitCurrentBranchAsync(repository.Root,
            "selected", "Writer", "writer@example.invalid", "develop", repository.Head, Patch, TestContext.Current.CancellationToken);
        result.Failure.Should().Be(GitCommandFailure.StagedChangesPresent);
        (await File.ReadAllBytesAsync(repository.Index, TestContext.Current.CancellationToken)).Should().Equal(index);
        (await File.ReadAllTextAsync(repository.File, TestContext.Current.CancellationToken)).Should().Be("unstaged\n");
        (await repository.Git("rev-parse", "HEAD")).Should().Be(repository.Head);
    }

    [Fact]
    public async Task SelectedCommit_OutsidePath_RejectsWithoutChangingHeadOrIndex()
    {
        using var repository = await Repository.CreateAsync("develop");
        var index = await File.ReadAllBytesAsync(repository.Index, TestContext.Current.CancellationToken);
        var unsafePatch = "diff --git a/../outside.txt b/../outside.txt\nnew file mode 100644\n--- /dev/null\n+++ b/../outside.txt\n@@ -0,0 +1 @@\n+outside\n";
        var result = await new GitCommandClient(new ProcessExecutor(), "unused.exe").CommitCurrentBranchAsync(repository.Root,
            "selected", "Writer", "writer@example.invalid", "develop", repository.Head, unsafePatch, TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeFalse();
        (await File.ReadAllBytesAsync(repository.Index, TestContext.Current.CancellationToken)).Should().Equal(index);
        (await repository.Git("rev-parse", "HEAD")).Should().Be(repository.Head);
    }

    [Theory]
    [InlineData("develop")]
    [InlineData("main")]
    [InlineData("feature/arbitrary")]
    public async Task Gateway_RegularAndSelectedCommit_OnlyUpdateCurrentBranchWithoutPolicyPermission(string branch)
    {
        using var repository = await Repository.CreateAsync(branch);
        await repository.Git("branch", "other");
        await repository.Git("update-ref", "refs/remotes/origin/other", repository.Head);
        var client = new GitCommandClient(new ProcessExecutor(), "unused.exe");
        var options = new RepositoryOptions("owner", "repo", repository.Root, "origin", "develop", "main",
            [], [], ["main"], "main", "^v[0-9]+$", "merge", true)
        { PullRequestRoutes = [], CommitAuthorName = "Writer", CommitAuthorEmail = "writer@example.invalid" };
        var gateway = new GitGateway(new RepositoryAllowlist(new Dictionary<string, RepositoryOptions> { ["fixture"] = options }),
            new LocalPathValidator(new RepositoryEnvironment()), client, new NoPrompt());
        await File.WriteAllTextAsync(repository.File, "regular\n", TestContext.Current.CancellationToken);
        var regular = await gateway.CommitAsync("fixture", "regular", TestContext.Current.CancellationToken);
        regular.IsSuccess.Should().BeTrue(regular.Diagnostic);
        regular.Value!.Branch.Should().Be(branch);
        (await repository.Git("show", "HEAD:file.txt")).Should().Be("regular");
        await File.WriteAllTextAsync(repository.File, "selected\nextra\n", TestContext.Current.CancellationToken);
        var selected = await gateway.CommitSelectedAsync("fixture", "selected", regular.Value.CommitSha,
            Patch.Replace("-base", "-regular", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        selected.IsSuccess.Should().BeTrue(selected.Diagnostic);
        selected.Value!.Branch.Should().Be(branch);
        (await repository.Git("rev-parse", "HEAD")).Should().Be(selected.Value.CommitSha);
        (await repository.Git("show", "HEAD:file.txt")).Should().Be("selected");
        (await File.ReadAllTextAsync(repository.File, TestContext.Current.CancellationToken)).Should().Be("selected\nextra\n");
        (await repository.Git("rev-parse", "refs/heads/other")).Should().Be(repository.Head);
        (await repository.Git("rev-parse", "refs/remotes/origin/other")).Should().Be(repository.Head);
        (await repository.Git("log", "-1", "--format=%an <%ae>")).Should().Be("Writer <writer@example.invalid>");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Commit_BranchChangesToEqualShaBeforeLock_RejectsAndPreservesIndex(bool selected)
    {
        using var repository = await Repository.CreateAsync("develop");
        await repository.Git("branch", "other");
        await File.WriteAllTextAsync(repository.File, "working\n", TestContext.Current.CancellationToken);
        var index = await File.ReadAllBytesAsync(repository.Index, TestContext.Current.CancellationToken);
        var intercepted = false;
        var executor = new InterceptExecutor(async arguments =>
        {
            if (!intercepted && arguments.Contains("--absolute-git-dir"))
            {
                intercepted = true;
                await repository.Git("symbolic-ref", "HEAD", "refs/heads/other");
            }
        });
        var result = await new GitCommandClient(executor, "unused.exe").CommitCurrentBranchAsync(repository.Root,
            "race", "Writer", "writer@example.invalid", "develop", repository.Head, selected ? Patch : null, TestContext.Current.CancellationToken);
        result.Failure.Should().Be(GitCommandFailure.BranchChanged);
        (await repository.Git("rev-parse", "refs/heads/develop")).Should().Be(repository.Head);
        (await repository.Git("rev-parse", "refs/heads/other")).Should().Be(repository.Head);
        (await File.ReadAllBytesAsync(repository.Index, TestContext.Current.CancellationToken)).Should().Equal(index);
        (await File.ReadAllTextAsync(repository.File, TestContext.Current.CancellationToken)).Should().Be("working\n");
        System.IO.File.Exists(repository.Index + ".lock").Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Commit_HeadChangesAtPublication_CasRejectsWithoutTouchingIndex(bool selected)
    {
        using var repository = await Repository.CreateAsync("develop");
        await File.WriteAllTextAsync(repository.File, "working\n", TestContext.Current.CancellationToken);
        var tree = await repository.Git("rev-parse", "HEAD^{tree}");
        var other = await repository.Git("-c", "user.name=Other", "-c", "user.email=other@example.invalid", "commit-tree", tree, "-p", repository.Head, "-m", "concurrent");
        var index = await File.ReadAllBytesAsync(repository.Index, TestContext.Current.CancellationToken);
        var changed = false;
        var executor = new InterceptExecutor(async arguments =>
        {
            if (!changed && arguments.Contains("update-ref"))
            {
                changed = true;
                await repository.Git("update-ref", "refs/heads/develop", other, repository.Head);
            }
        });
        var result = await new GitCommandClient(executor, "unused.exe").CommitCurrentBranchAsync(repository.Root,
            "race", "Writer", "writer@example.invalid", "develop", repository.Head, selected ? Patch : null, TestContext.Current.CancellationToken);
        result.Failure.Should().Be(GitCommandFailure.HeadChanged);
        (await repository.Git("rev-parse", "HEAD")).Should().Be(other);
        (await File.ReadAllBytesAsync(repository.Index, TestContext.Current.CancellationToken)).Should().Equal(index);
        (await File.ReadAllTextAsync(repository.File, TestContext.Current.CancellationToken)).Should().Be("working\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Commit_DetachedHead_DoesNotCreateOrSwitchBranches(bool selected)
    {
        using var repository = await Repository.CreateAsync("develop");
        await repository.Git("checkout", "--detach", repository.Head);
        var result = await new GitCommandClient(new ProcessExecutor(), "unused.exe").CommitCurrentBranchAsync(repository.Root,
            "detached", "Writer", "writer@example.invalid", "develop", repository.Head, selected ? Patch : null, TestContext.Current.CancellationToken);
        result.Failure.Should().Be(GitCommandFailure.DetachedHead);
        (await repository.Git("rev-parse", "HEAD")).Should().Be(repository.Head);
        (await repository.Git("for-each-ref", "--format=%(refname)", "refs/heads/")).Should().Be("refs/heads/develop");
    }

    [Theory]
    [InlineData("HEAD.lock")]
    [InlineData("index.lock")]
    public async Task Commit_ForeignGitLock_IsNeverDeleted(string lockName)
    {
        using var repository = await Repository.CreateAsync("develop");
        var lockPath = Path.Combine(repository.Root, ".git", lockName);
        await File.WriteAllTextAsync(repository.File, "working\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(lockPath, "foreign owner", TestContext.Current.CancellationToken);
        var result = await new GitCommandClient(new ProcessExecutor(), "unused.exe").CommitCurrentBranchAsync(repository.Root,
            "locked", "Writer", "writer@example.invalid", "develop", repository.Head, null, TestContext.Current.CancellationToken);
        result.Failure.Should().Be(GitCommandFailure.RepositoryBusy);
        (await File.ReadAllTextAsync(lockPath, TestContext.Current.CancellationToken)).Should().Be("foreign owner");
    }

    [Fact]
    public async Task RegularCommit_FailedPreparation_PreservesStagedAndUnstagedContent()
    {
        using var repository = await Repository.CreateAsync("develop");
        await File.WriteAllTextAsync(repository.File, "staged\n", TestContext.Current.CancellationToken);
        await repository.Git("add", "--", "file.txt");
        await File.WriteAllTextAsync(repository.File, "unstaged\n", TestContext.Current.CancellationToken);
        var index = await File.ReadAllBytesAsync(repository.Index, TestContext.Current.CancellationToken);
        var executor = new FailWriteTreeExecutor();
        var result = await new GitCommandClient(executor, "unused.exe").CommitCurrentBranchAsync(repository.Root,
            "failure", "Writer", "writer@example.invalid", "develop", repository.Head, null, TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeFalse();
        (await repository.Git("rev-parse", "HEAD")).Should().Be(repository.Head);
        (await File.ReadAllBytesAsync(repository.Index, TestContext.Current.CancellationToken)).Should().Equal(index);
        (await repository.Git("show", ":file.txt")).Should().Be("staged");
        (await File.ReadAllTextAsync(repository.File, TestContext.Current.CancellationToken)).Should().Be("unstaged\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Commit_SymbolicHeadChangeDuringPreparation_IsBlockedWithoutRedirectingCommit(bool selected)
    {
        using var repository = await Repository.CreateAsync("develop");
        await repository.Git("branch", "other");
        await File.WriteAllTextAsync(repository.File, "working\n", TestContext.Current.CancellationToken);
        GitCommandResult? attemptedSwitch = null;
        var executor = new InterceptExecutor(async arguments =>
        {
            if (attemptedSwitch is null && arguments.Contains("write-tree"))
                attemptedSwitch = await new ProcessExecutor().ExecuteAsync(repository.Root, "git",
                    ["symbolic-ref", "HEAD", "refs/heads/other"], GitEnvironmentSanitizer.BuildBaseEnvironment(),
                    TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        });
        var result = await new GitCommandClient(executor, "unused.exe").CommitCurrentBranchAsync(repository.Root,
            "guarded", "Writer", "writer@example.invalid", "develop", repository.Head, selected ? Patch : null, TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue(result.StandardError);
        attemptedSwitch.Should().NotBeNull();
        attemptedSwitch!.IsSuccess.Should().BeFalse();
        (await repository.Git("symbolic-ref", "HEAD")).Should().Be("refs/heads/develop");
        (await repository.Git("rev-parse", "refs/heads/other")).Should().Be(repository.Head);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Commit_IndexPublicationFails_RollsBackOrReportsUnknownWithoutChangingIndex(bool failRollback)
    {
        using var repository = await Repository.CreateAsync("develop");
        await File.WriteAllTextAsync(repository.File, "working\n", TestContext.Current.CancellationToken);
        var index = await File.ReadAllBytesAsync(repository.Index, TestContext.Current.CancellationToken);
        using var executor = new BlockIndexExecutor(repository.Index, failRollback);
        var result = await new GitCommandClient(executor, "unused.exe").CommitCurrentBranchAsync(repository.Root,
            "index failure", "Writer", "writer@example.invalid", "develop", repository.Head, null, TestContext.Current.CancellationToken);
        result.Failure.Should().Be(failRollback ? GitCommandFailure.CommitResultUnknown : GitCommandFailure.IndexUpdateFailed);
        (await File.ReadAllBytesAsync(repository.Index, TestContext.Current.CancellationToken)).Should().Equal(index);
        (await File.ReadAllTextAsync(repository.File, TestContext.Current.CancellationToken)).Should().Be("working\n");
        var head = await repository.Git("rev-parse", "HEAD");
        if (failRollback) head.Should().NotBe(repository.Head);
        else head.Should().Be(repository.Head);
    }

    private sealed class BlockIndexExecutor(string indexPath, bool failRollback) : IProcessExecutor, IDisposable
    {
        private FileStream? _reader;
        public async Task<GitCommandResult> ExecuteAsync(string workingDirectory, string fileName, IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environmentOverrides, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var updating = arguments.Contains("update-ref");
            if (updating && _reader is not null && failRollback) return GitCommandResult.Failed(GitCommandFailure.Failed);
            var result = await new ProcessExecutor().ExecuteAsync(workingDirectory, fileName, arguments, environmentOverrides, timeout, cancellationToken);
            if (updating && _reader is null && result.IsSuccess)
                _reader = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return result;
        }
        public void Dispose() => _reader?.Dispose();
    }

    private sealed class InterceptExecutor(Func<IReadOnlyList<string>, Task> before) : IProcessExecutor
    {
        private readonly ProcessExecutor _inner = new();
        public async Task<GitCommandResult> ExecuteAsync(string workingDirectory, string fileName, IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environmentOverrides, TimeSpan timeout, CancellationToken cancellationToken)
        {
            await before(arguments);
            return await _inner.ExecuteAsync(workingDirectory, fileName, arguments, environmentOverrides, timeout, cancellationToken);
        }
    }

    private sealed class FailWriteTreeExecutor : IProcessExecutor
    {
        public Task<GitCommandResult> ExecuteAsync(string workingDirectory, string fileName, IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environmentOverrides, TimeSpan timeout, CancellationToken cancellationToken) =>
            arguments.Contains("write-tree") ? Task.FromResult(GitCommandResult.Failed(GitCommandFailure.Failed))
                : new ProcessExecutor().ExecuteAsync(workingDirectory, fileName, arguments, environmentOverrides, timeout, cancellationToken);
    }

    private sealed class NoPrompt : IInteractiveApprovalPrompt
    {
        public Task<ApprovalPromptOutcome> RequestApprovalAsync(ApprovalPromptRequest request, TimeSpan timeout, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Ordinary local commit does not require an approval prompt.");
    }

    private sealed class Repository : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("githubie-current-commit-").FullName;
        public string File => Path.Combine(Root, "file.txt");
        public string Index => Path.Combine(Root, ".git", "index");
        public string Head { get; private set; } = string.Empty;
        public static async Task<Repository> CreateAsync(string branch)
        {
            var repository = new Repository();
            await repository.Git("init", "--initial-branch=" + branch);
            await System.IO.File.WriteAllTextAsync(repository.File, "base\n", TestContext.Current.CancellationToken);
            await repository.Git("add", "--", "file.txt");
            await repository.Git("-c", "user.name=Bootstrap", "-c", "user.email=bootstrap@example.invalid", "commit", "-m", "base");
            repository.Head = await repository.Git("rev-parse", "HEAD");
            return repository;
        }
        public async Task<string> Git(params string[] arguments)
        {
            var result = await new ProcessExecutor().ExecuteAsync(Root, "git", arguments,
                GitEnvironmentSanitizer.BuildBaseEnvironment(), TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            result.IsSuccess.Should().BeTrue(result.StandardError);
            return result.StandardOutput.Trim();
        }
        public void Dispose()
        {
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                System.IO.File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }
}
