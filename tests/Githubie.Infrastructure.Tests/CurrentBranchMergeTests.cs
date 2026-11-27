using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.Git;
using Githubie.Application.Interactive;
using Githubie.Application.Repositories;
using Githubie.Infrastructure.Git;
using Githubie.Infrastructure.Repositories;
using Xunit;

namespace Githubie.Infrastructure.Tests;

/// <summary>実Gitリポジトリで github_repository_merge の取り込み・拒否・復元を検証します。</summary>
public sealed class CurrentBranchMergeTests
{
    private const string Id = "fixture";

    [Fact]
    public async Task CleanMerge_CreatesMergeCommitWithBothParents_AndDoesNotPush()
    {
        using var repo = await Fixture.CreateDivergedAsync(conflict: false);
        var remoteBefore = await repo.Git("ls-remote", "origin");

        var result = await repo.Gateway().MergeAsync(Id, "develop", repo.DevelopHead, GitMergeSourceKind.Branch, "main",
            repo.MainHead, GitMergeFastForwardMode.Allow, "merge main", null, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(result.Diagnostic);
        var info = result.Value!;
        info.FastForward.Should().BeFalse();
        info.Parents.Should().Equal(repo.DevelopHead, repo.MainHead);
        info.PreviousHead.Should().Be(repo.DevelopHead);
        (await repo.Git("rev-parse", "HEAD")).Should().Be(info.CommitSha);
        (await repo.Git("rev-parse", "HEAD^1")).Should().Be(repo.DevelopHead);
        (await repo.Git("rev-parse", "HEAD^2")).Should().Be(repo.MainHead);
        (await repo.Git("log", "-1", "--format=%s|%an <%ae>")).Should().Be("merge main|Writer <writer@example.invalid>");
        (await File.ReadAllTextAsync(repo.PathOf("main.txt"), TestContext.Current.CancellationToken)).Should().Be("main\n");
        (await File.ReadAllTextAsync(repo.PathOf("develop.txt"), TestContext.Current.CancellationToken)).Should().Be("develop\n");
        (await repo.Git("status", "--porcelain", "--untracked-files=all")).Should().BeEmpty();
        (await repo.Git("ls-remote", "origin")).Should().Be(remoteBefore, "merge must never push");
        File.Exists(Path.Combine(repo.Root, ".git", "MERGE_HEAD")).Should().BeFalse();
    }

    [Theory]
    [InlineData(GitMergeFastForwardMode.Allow, true)]
    [InlineData(GitMergeFastForwardMode.Only, true)]
    [InlineData(GitMergeFastForwardMode.Never, false)]
    public async Task FastForwardableSource_FollowsExplicitFastForwardMode(string mode, bool expectFastForward)
    {
        using var repo = await Fixture.CreateFastForwardableAsync();

        var result = await repo.Gateway().MergeAsync(Id, "develop", repo.DevelopHead, GitMergeSourceKind.Branch, "main",
            repo.MainHead, mode, null, null, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(result.Diagnostic);
        result.Value!.FastForward.Should().Be(expectFastForward);
        var head = await repo.Git("rev-parse", "HEAD");
        if (expectFastForward)
        {
            head.Should().Be(repo.MainHead);
            result.Value.Parents.Should().Equal(repo.DevelopHead);
        }
        else
        {
            head.Should().NotBe(repo.MainHead);
            result.Value.Parents.Should().Equal(repo.DevelopHead, repo.MainHead);
        }
        (await File.ReadAllTextAsync(repo.PathOf("main.txt"), TestContext.Current.CancellationToken)).Should().Be("main\n");
        (await repo.Git("status", "--porcelain", "--untracked-files=all")).Should().BeEmpty();
    }

    [Fact]
    public async Task FastForwardOnly_WhenDiverged_RejectsWithoutChanges()
    {
        using var repo = await Fixture.CreateDivergedAsync(conflict: false);
        var snapshot = await repo.SnapshotAsync();

        var result = await repo.Gateway().MergeAsync(Id, "develop", repo.DevelopHead, GitMergeSourceKind.Branch, "main",
            repo.MainHead, GitMergeFastForwardMode.Only, null, null, TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitGatewayError.FastForwardNotPossible);
        (await repo.SnapshotAsync()).Should().Be(snapshot);
    }

    [Fact]
    public async Task Conflict_DefaultAbortsAndRestoresEverything_ListingConflictedPaths()
    {
        using var repo = await Fixture.CreateDivergedAsync(conflict: true);
        var snapshot = await repo.SnapshotAsync();

        var result = await repo.Gateway().MergeAsync(Id, "develop", repo.DevelopHead, GitMergeSourceKind.Branch, "main",
            repo.MainHead, GitMergeFastForwardMode.Allow, null, null, TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitGatewayError.MergeConflict);
        result.Candidates.Should().Equal("README.md", "removed.txt");
        (await repo.SnapshotAsync()).Should().Be(snapshot);
        File.Exists(Path.Combine(repo.Root, ".git", "MERGE_HEAD")).Should().BeFalse();
        Directory.EnumerateFiles(Path.Combine(repo.Root, ".git"), "githubie-merge-*").Should().BeEmpty();
        File.Exists(Path.Combine(repo.Root, ".git", "index.lock")).Should().BeFalse();
    }

    [Fact]
    public async Task Conflict_WithPerFileResolutions_CommitsExplicitlyChosenSides()
    {
        using var repo = await Fixture.CreateDivergedAsync(conflict: true);

        var result = await repo.Gateway().MergeAsync(Id, "develop", repo.DevelopHead, GitMergeSourceKind.Branch, "main",
            repo.MainHead, GitMergeFastForwardMode.Allow, null,
            [new("README.md", GitMergeResolution.Source), new("removed.txt", GitMergeResolution.Current)],
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(result.Diagnostic);
        result.Value!.ResolvedPaths.Should().Equal("README.md", "removed.txt");
        result.Value.Parents.Should().Equal(repo.DevelopHead, repo.MainHead);
        (await repo.Git("show", "HEAD:README.md")).Should().Be("main readme");
        (await repo.Git("show", "HEAD:removed.txt")).Should().Be("changed on develop");
        (await File.ReadAllTextAsync(repo.PathOf("README.md"), TestContext.Current.CancellationToken)).Should().Be("main readme\n");
        (await repo.Git("status", "--porcelain", "--untracked-files=all")).Should().BeEmpty();
    }

    [Fact]
    public async Task Conflict_WithContentAndDeleteResolutions_CommitsSuppliedState()
    {
        using var repo = await Fixture.CreateDivergedAsync(conflict: true);

        var result = await repo.Gateway().MergeAsync(Id, "develop", repo.DevelopHead, GitMergeSourceKind.Commit, repo.MainHead,
            repo.MainHead, GitMergeFastForwardMode.Never, "resolve",
            [new("README.md", GitMergeResolution.FullContent, "merged readme\n"), new("removed.txt", GitMergeResolution.Source)],
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(result.Diagnostic);
        (await repo.Git("show", "HEAD:README.md")).Should().Be("merged readme");
        File.Exists(repo.PathOf("removed.txt")).Should().BeFalse("the source side deleted the file");
        (await repo.Git("ls-tree", "--name-only", "HEAD")).Split('\n').Should().NotContain("removed.txt");
        (await repo.Git("status", "--porcelain", "--untracked-files=all")).Should().BeEmpty();
    }

    [Fact]
    public async Task Conflict_WithAnUnresolvedPath_DoesNotCommitAndListsOnlyUnresolved()
    {
        using var repo = await Fixture.CreateDivergedAsync(conflict: true);
        var snapshot = await repo.SnapshotAsync();

        var result = await repo.Gateway().MergeAsync(Id, "develop", repo.DevelopHead, GitMergeSourceKind.Branch, "main",
            repo.MainHead, GitMergeFastForwardMode.Allow, null, [new("README.md", GitMergeResolution.Current)],
            TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitGatewayError.MergeConflict);
        result.Candidates.Should().Equal("removed.txt");
        (await repo.SnapshotAsync()).Should().Be(snapshot);
    }

    [Fact]
    public async Task ResolutionForNonConflictedPath_IsRejectedWithoutChanges()
    {
        using var repo = await Fixture.CreateDivergedAsync(conflict: true);
        var snapshot = await repo.SnapshotAsync();

        var result = await repo.Gateway().MergeAsync(Id, "develop", repo.DevelopHead, GitMergeSourceKind.Branch, "main",
            repo.MainHead, GitMergeFastForwardMode.Allow, null,
            [new("README.md", GitMergeResolution.Current), new("removed.txt", GitMergeResolution.Current), new("develop.txt", GitMergeResolution.Source)],
            TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitGatewayError.MergeResolutionInvalid);
        (await repo.SnapshotAsync()).Should().Be(snapshot);
    }

    [Fact]
    public async Task DirtyWorkingTree_IncludingUntracked_IsRejectedBeforeSideEffects()
    {
        using var repo = await Fixture.CreateDivergedAsync(conflict: false);
        await File.WriteAllTextAsync(repo.PathOf("untracked.txt"), "x\n", TestContext.Current.CancellationToken);
        var snapshot = await repo.SnapshotAsync();

        var result = await repo.Gateway().MergeAsync(Id, "develop", repo.DevelopHead, GitMergeSourceKind.Branch, "main",
            repo.MainHead, GitMergeFastForwardMode.Allow, null, null, TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitGatewayError.WorkingTreeDirty);
        (await repo.SnapshotAsync()).Should().Be(snapshot);
    }

    [Fact]
    public async Task DetachedHead_IsRejectedBeforeSideEffects()
    {
        using var repo = await Fixture.CreateDivergedAsync(conflict: false);
        await repo.Git("checkout", "--detach", "develop");
        var snapshot = await repo.SnapshotAsync();

        var result = await repo.Gateway().MergeAsync(Id, "develop", repo.DevelopHead, GitMergeSourceKind.Branch, "main",
            repo.MainHead, GitMergeFastForwardMode.Allow, null, null, TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitGatewayError.DetachedHead);
        (await repo.SnapshotAsync()).Should().Be(snapshot);
    }

    [Fact]
    public async Task ExpectedHeadMismatch_IsRejectedBeforeSideEffects()
    {
        using var repo = await Fixture.CreateDivergedAsync(conflict: false);
        var snapshot = await repo.SnapshotAsync();

        var result = await repo.Gateway().MergeAsync(Id, "develop", repo.MainHead, GitMergeSourceKind.Branch, "main",
            repo.MainHead, GitMergeFastForwardMode.Allow, null, null, TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitGatewayError.ExpectedHeadMismatch);
        (await repo.SnapshotAsync()).Should().Be(snapshot);
    }

    [Fact]
    public async Task CurrentBranchDifferentFromRequestedBranch_IsRejectedBeforeSideEffects()
    {
        using var repo = await Fixture.CreateDivergedAsync(conflict: false);
        await repo.Git("branch", "feature", "develop");
        var snapshot = await repo.SnapshotAsync();

        var result = await repo.Gateway().MergeAsync(Id, "feature", repo.DevelopHead, GitMergeSourceKind.Branch, "main",
            repo.MainHead, GitMergeFastForwardMode.Allow, null, null, TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitGatewayError.CurrentBranchChanged);
        (await repo.SnapshotAsync()).Should().Be(snapshot);
    }

    [Fact]
    public async Task ExpectedSourceMismatch_IsRejectedBeforeSideEffects()
    {
        using var repo = await Fixture.CreateDivergedAsync(conflict: false);
        var snapshot = await repo.SnapshotAsync();

        var result = await repo.Gateway().MergeAsync(Id, "develop", repo.DevelopHead, GitMergeSourceKind.Branch, "main",
            repo.BaseHead, GitMergeFastForwardMode.Allow, null, null, TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitGatewayError.ExpectedSourceMismatch);
        (await repo.SnapshotAsync()).Should().Be(snapshot);
    }

    [Theory]
    [InlineData(GitMergeSourceKind.Branch, "missing")]
    [InlineData(GitMergeSourceKind.Tag, "main")]
    [InlineData(GitMergeSourceKind.RemoteBranch, "origin/missing")]
    [InlineData(GitMergeSourceKind.Commit, "0123456789abcdef0123456789abcdef01234567")]
    public async Task MissingSource_IsRejectedBeforeSideEffects(string kind, string source)
    {
        using var repo = await Fixture.CreateDivergedAsync(conflict: false);
        var snapshot = await repo.SnapshotAsync();

        var result = await repo.Gateway().MergeAsync(Id, "develop", repo.DevelopHead, kind, source,
            repo.MainHead, GitMergeFastForwardMode.Allow, null, null, TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitGatewayError.MergeSourceNotFound);
        (await repo.SnapshotAsync()).Should().Be(snapshot);
    }

    [Fact]
    public async Task RemoteTrackingBranchAndTagSources_AreResolvedOnlyAsExplicitlySpecified()
    {
        using var repo = await Fixture.CreateDivergedAsync(conflict: false);
        await repo.Git("fetch", "origin");
        await repo.Git("tag", "v-main", "main");

        var remote = await repo.Gateway().MergeAsync(Id, "develop", repo.DevelopHead, GitMergeSourceKind.RemoteBranch, "origin/main",
            repo.MainHead, GitMergeFastForwardMode.Never, null, null, TestContext.Current.CancellationToken);
        remote.IsSuccess.Should().BeTrue(remote.Diagnostic);
        remote.Value!.SourceSha.Should().Be(repo.MainHead);

        await repo.Git("reset", "--hard", repo.DevelopHead);
        var tag = await repo.Gateway().MergeAsync(Id, "develop", repo.DevelopHead, GitMergeSourceKind.Tag, "v-main",
            repo.MainHead, GitMergeFastForwardMode.Never, null, null, TestContext.Current.CancellationToken);
        tag.IsSuccess.Should().BeTrue(tag.Diagnostic);
    }

    [Fact]
    public async Task ProtectedBranchTarget_IsRejectedBeforeSideEffects()
    {
        using var repo = await Fixture.CreateDivergedAsync(conflict: false);
        await repo.Git("switch", "main");
        var snapshot = await repo.SnapshotAsync();

        var result = await repo.Gateway().MergeAsync(Id, "main", repo.MainHead, GitMergeSourceKind.Branch, "develop",
            repo.DevelopHead, GitMergeFastForwardMode.Allow, null, null, TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitGatewayError.ProtectedBranch);
        (await repo.SnapshotAsync()).Should().Be(snapshot);
    }

    [Fact]
    public async Task SourceAlreadyContained_IsReportedWithoutChanges()
    {
        using var repo = await Fixture.CreateFastForwardableAsync();
        await repo.Git("reset", "--hard", repo.MainHead);
        var snapshot = await repo.SnapshotAsync();

        var result = await repo.Gateway().MergeAsync(Id, "develop", repo.MainHead, GitMergeSourceKind.Commit, repo.BaseHead,
            repo.BaseHead, GitMergeFastForwardMode.Allow, null, null, TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitGatewayError.NothingToMerge);
        (await repo.SnapshotAsync()).Should().Be(snapshot);
    }

    [Theory]
    [InlineData("", "develop", "invalid_kind", GitMergeFastForwardMode.Allow)]
    [InlineData("develop", "-main", GitMergeSourceKind.Branch, GitMergeFastForwardMode.Allow)]
    [InlineData("develop", "main", "guess", GitMergeFastForwardMode.Allow)]
    [InlineData("develop", "main", GitMergeSourceKind.Branch, "maybe")]
    [InlineData("develop", "main", GitMergeSourceKind.Commit, GitMergeFastForwardMode.Allow)]
    public async Task InvalidRequest_IsRejectedBeforeTouchingTheRepository(string branch, string source, string kind, string mode)
    {
        using var repo = await Fixture.CreateDivergedAsync(conflict: false);
        var snapshot = await repo.SnapshotAsync();

        var result = await repo.Gateway().MergeAsync(Id, branch, repo.DevelopHead, kind, source,
            repo.MainHead, mode, null, null, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOneOf(GitGatewayError.InvalidMergeRequest, GitGatewayError.InvalidRef);
        (await repo.SnapshotAsync()).Should().Be(snapshot);
    }

    private sealed class NoPrompt : IInteractiveApprovalPrompt
    {
        public Task<ApprovalPromptOutcome> RequestApprovalAsync(ApprovalPromptRequest request, TimeSpan timeout, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Local merge does not require an approval prompt.");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _container = Directory.CreateTempSubdirectory("githubie-merge-").FullName;
        public string Root => Path.Combine(_container, "work");
        private string Bare => Path.Combine(_container, "origin.git");
        public string BaseHead { get; private set; } = string.Empty;
        public string DevelopHead { get; private set; } = string.Empty;
        public string MainHead { get; private set; } = string.Empty;
        public string PathOf(string name) => Path.Combine(Root, name);

        public GitGateway Gateway()
        {
            var options = new RepositoryOptions("owner", "repo", Root, "origin", "develop", "main",
                ["develop"], ["develop", "main"], ["main"], "main", "^v[0-9]+$", "merge", true)
            { PullRequestRoutes = [], CommitAuthorName = "Writer", CommitAuthorEmail = "writer@example.invalid" };
            return new GitGateway(new RepositoryAllowlist(new Dictionary<string, RepositoryOptions> { [Id] = options }),
                new LocalPathValidator(new RepositoryEnvironment()), new GitCommandClient(new ProcessExecutor(), "unused.exe"), new NoPrompt());
        }

        /// <summary>developとmainがbaseから分岐した状態。conflict=trueでREADME.md（内容）とremoved.txt（変更/削除）が衝突します。</summary>
        public static async Task<Fixture> CreateDivergedAsync(bool conflict)
        {
            var fixture = new Fixture();
            await fixture.InitializeAsync();
            await fixture.Commit("base", ("README.md", "base readme\n"), ("removed.txt", "base\n"));
            fixture.BaseHead = await fixture.Git("rev-parse", "HEAD");
            await fixture.Git("branch", "main");
            if (conflict)
                await fixture.Commit("develop", ("README.md", "develop readme\n"), ("removed.txt", "changed on develop\n"), ("develop.txt", "develop\n"));
            else
                await fixture.Commit("develop", ("develop.txt", "develop\n"));
            fixture.DevelopHead = await fixture.Git("rev-parse", "HEAD");
            await fixture.Git("switch", "main");
            if (conflict)
            {
                File.Delete(fixture.PathOf("removed.txt"));
                await fixture.Commit("main", ("README.md", "main readme\n"), ("main.txt", "main\n"));
            }
            else await fixture.Commit("main", ("main.txt", "main\n"));
            fixture.MainHead = await fixture.Git("rev-parse", "HEAD");
            await fixture.Git("switch", "develop");
            await fixture.Git("push", "origin", "develop", "main");
            return fixture;
        }

        /// <summary>mainがdevelopの子孫（fast-forward可能）な状態。</summary>
        public static async Task<Fixture> CreateFastForwardableAsync()
        {
            var fixture = new Fixture();
            await fixture.InitializeAsync();
            await fixture.Commit("base", ("README.md", "base readme\n"));
            fixture.BaseHead = await fixture.Git("rev-parse", "HEAD");
            fixture.DevelopHead = fixture.BaseHead;
            await fixture.Git("switch", "-c", "main");
            await fixture.Commit("main", ("main.txt", "main\n"));
            fixture.MainHead = await fixture.Git("rev-parse", "HEAD");
            await fixture.Git("switch", "develop");
            await fixture.Git("push", "origin", "develop", "main");
            return fixture;
        }

        /// <summary>HEAD・branch ref・index・作業ツリーの状態を比較可能な文字列で返します。</summary>
        public async Task<string> SnapshotAsync()
        {
            var head = await Git("rev-parse", "HEAD");
            var symbolic = await GitAllowFailure("symbolic-ref", "--quiet", "HEAD");
            var refs = await Git("for-each-ref", "--format=%(refname) %(objectname)");
            var index = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                await File.ReadAllBytesAsync(Path.Combine(Root, ".git", "index"), TestContext.Current.CancellationToken)));
            var files = string.Join("\n", Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .Select(path => $"{Path.GetRelativePath(Root, path)}={File.ReadAllText(path)}"));
            return string.Join("\n---\n", head, symbolic, refs, index, files);
        }

        private async Task InitializeAsync()
        {
            Directory.CreateDirectory(Root);
            await Run(_container, "init", "--bare", "--initial-branch=develop", Bare);
            await Git("init", "--initial-branch=develop");
            await Git("remote", "add", "origin", Bare);
            await Git("config", "core.autocrlf", "false");
        }

        private async Task Commit(string message, params (string Name, string Content)[] files)
        {
            foreach (var (name, content) in files) await File.WriteAllTextAsync(PathOf(name), content, TestContext.Current.CancellationToken);
            await Git("add", "--all", "--");
            await Git("-c", "user.name=Bootstrap", "-c", "user.email=bootstrap@example.invalid", "commit", "-m", message);
        }

        public Task<string> Git(params string[] arguments) => Run(Root, arguments);

        private async Task<string> GitAllowFailure(params string[] arguments)
        {
            var result = await new ProcessExecutor().ExecuteAsync(Root, "git", arguments,
                GitEnvironmentSanitizer.BuildBaseEnvironment(), TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            return result.IsSuccess ? result.StandardOutput.Trim() : $"exit:{result.ExitCode}";
        }

        private static async Task<string> Run(string directory, params string[] arguments)
        {
            var result = await new ProcessExecutor().ExecuteAsync(directory, "git", arguments,
                GitEnvironmentSanitizer.BuildBaseEnvironment(), TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            result.IsSuccess.Should().BeTrue(result.StandardError);
            return result.StandardOutput.Trim();
        }

        public void Dispose()
        {
            foreach (var file in Directory.EnumerateFiles(_container, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_container, recursive: true);
        }
    }
}
