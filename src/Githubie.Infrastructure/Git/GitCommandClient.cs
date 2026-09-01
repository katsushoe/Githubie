using Githubie.Application.Git;

namespace Githubie.Infrastructure.Git;

/// <summary>
/// <see cref="IGitCommandClient"/>の実装です。許可された固定引数配列だけをGitへ渡します。
/// Agent入力をGit Argumentへ直接連結せず、remote名/branch名の前に`--`を置いてoption injectionを防ぎます。
/// </summary>
public sealed class GitCommandClient(IProcessExecutor processExecutor, string askPassExecutablePath) : IGitCommandClient
{
    private static readonly TimeSpan LocalTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(120);

    private readonly IProcessExecutor _processExecutor = processExecutor;
    private readonly string _askPassExecutablePath = askPassExecutablePath;

    public async Task<GitCommandResult> GetCurrentBranchAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var result = await ExecuteLocalAsync(repositoryRoot, ["rev-parse", "--abbrev-ref", "HEAD"], cancellationToken);
        return result.IsSuccess
            ? result
            : await ExecuteLocalAsync(repositoryRoot, ["symbolic-ref", "--short", "HEAD"], cancellationToken);
    }

    public async Task<GitCommandResult> GetHeadAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var result = await ExecuteLocalAsync(repositoryRoot, ["rev-parse", "--verify", "--quiet", "HEAD"], cancellationToken);
        return IsUnbornHead(result) ? GitCommandResult.Success(string.Empty) : result;
    }

    public Task<GitCommandResult> GetRemoteHeadAsync(string repositoryRoot, string remote, string branch, CancellationToken cancellationToken) =>
        ExecuteLocalAsync(repositoryRoot, ["rev-parse", $"refs/remotes/{remote}/{branch}"], cancellationToken);

    public Task<GitCommandResult> GetAheadBehindAsync(string repositoryRoot, string remote, string branch, CancellationToken cancellationToken) =>
        ExecuteLocalAsync(repositoryRoot, ["rev-list", "--left-right", "--count", $"HEAD...refs/remotes/{remote}/{branch}"], cancellationToken);

    public Task<GitCommandResult> GetStatusAsync(string repositoryRoot, CancellationToken cancellationToken) =>
        ExecuteLocalAsync(repositoryRoot, ["status", "--porcelain"], cancellationToken);

    public async Task<GitCommandResult> GetDiffAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var head = await GetHeadAsync(repositoryRoot, cancellationToken);
        if (!head.IsSuccess)
        {
            return head;
        }

        var arguments = head.StandardOutput.Length == 0
            ? new[] { "diff", "--no-ext-diff", "--binary", "--cached", "--" }
            : new[] { "diff", "--no-ext-diff", "--binary", "HEAD", "--" };
        return await ExecuteLocalAsync(repositoryRoot, arguments, cancellationToken);
    }

    public Task<GitCommandResult> AddAllAsync(string repositoryRoot, CancellationToken cancellationToken) =>
        ExecuteLocalAsync(repositoryRoot, ["add", "--all", "--"], cancellationToken);

    public Task<GitCommandResult> CommitAsync(string repositoryRoot, string message, CancellationToken cancellationToken) =>
        ExecuteLocalAsync(repositoryRoot, ["commit", "-m", message, "--"], cancellationToken);

    public Task<GitCommandResult> GetRemoteUrlAsync(string repositoryRoot, string remote, CancellationToken cancellationToken) =>
        ExecuteLocalAsync(repositoryRoot, ["remote", "get-url", "--", remote], cancellationToken);

    public Task<GitCommandResult> FetchAsync(string repositoryRoot, string repositoryId, string remote, CancellationToken cancellationToken) =>
        ExecuteNetworkAsync(repositoryRoot, repositoryId, ["fetch", "--", remote], cancellationToken);

    public Task<GitCommandResult> PullFastForwardOnlyAsync(string repositoryRoot, string repositoryId, string remote, string branch, CancellationToken cancellationToken) =>
        ExecuteNetworkAsync(repositoryRoot, repositoryId, ["pull", "--ff-only", "--", remote, branch], cancellationToken);

    public Task<GitCommandResult> PushAsync(string repositoryRoot, string repositoryId, string remote, string branch, CancellationToken cancellationToken) =>
        ExecuteNetworkAsync(repositoryRoot, repositoryId, ["push", "--", remote, branch], cancellationToken);

    public Task<GitCommandResult> PushTagAsync(string repositoryRoot, string repositoryId, string remote, string tag, CancellationToken cancellationToken) =>
        ExecuteNetworkAsync(repositoryRoot, repositoryId, ["push", "--", remote, $"refs/tags/{tag}:refs/tags/{tag}"], cancellationToken);

    public Task<GitCommandResult> GetLocalRefAsync(string repositoryRoot, string reference, CancellationToken cancellationToken) =>
        ExecuteLocalAsync(repositoryRoot, ["rev-parse", "--verify", reference], cancellationToken);

    public Task<GitCommandResult> GetRemoteRefAsync(string repositoryRoot, string repositoryId, string remote, string reference, CancellationToken cancellationToken) =>
        ExecuteNetworkAsync(repositoryRoot, repositoryId, ["ls-remote", "--refs", "--", remote, reference], cancellationToken);

    public Task<GitCommandResult> PushHistoryRewriteAsync(
        string repositoryRoot,
        string repositoryId,
        string remote,
        IReadOnlyList<GitHistoryRewriteRef> refs,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "push", "--atomic" };
        foreach (var item in refs)
        {
            arguments.Add($"--force-with-lease={item.Ref}:{item.ExpectedRemoteSha}");
        }
        arguments.Add("--");
        arguments.Add(remote);
        foreach (var item in refs)
        {
            arguments.Add($"{item.NewLocalSha}:{item.Ref}");
        }
        return ExecuteNetworkAsync(repositoryRoot, repositoryId, arguments, cancellationToken);
    }

    private Task<GitCommandResult> ExecuteLocalAsync(string repositoryRoot, IReadOnlyList<string> gitArguments, CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "-c", $"safe.directory={NormalizeSafeDirectory(repositoryRoot)}" };
        arguments.AddRange(gitArguments);

        return _processExecutor.ExecuteAsync(repositoryRoot, "git", arguments, GitEnvironmentSanitizer.BuildBaseEnvironment(), LocalTimeout, cancellationToken);
    }

    private Task<GitCommandResult> ExecuteNetworkAsync(string repositoryRoot, string repositoryId, IReadOnlyList<string> gitArguments, CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "-c", $"safe.directory={NormalizeSafeDirectory(repositoryRoot)}",
            "-c", "credential.helper=",
        };
        arguments.AddRange(gitArguments);

        var environment = GitEnvironmentSanitizer.BuildBaseEnvironment();
        foreach (var (key, value) in GitAskPassProtocol.CreateEnvironment(_askPassExecutablePath, repositoryId))
        {
            environment[key] = value;
        }

        return _processExecutor.ExecuteAsync(repositoryRoot, "git", arguments, environment, NetworkTimeout, cancellationToken);
    }

    private static string NormalizeSafeDirectory(string repositoryRoot) => repositoryRoot.Replace('\\', '/');

    private static bool IsUnbornHead(GitCommandResult result) =>
        !result.IsSuccess &&
        result.Failure == GitCommandFailure.Failed &&
        result.ExitCode == 1 &&
        result.StandardOutput.Length == 0 &&
        result.StandardError.Length == 0;
}
