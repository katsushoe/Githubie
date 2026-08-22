using Githubie.Application.Configuration;
using Githubie.Application.Git;
using Githubie.Application.Interactive;

namespace Githubie.Application.Repositories;

/// <summary>Git remote由来の情報だけを用いてRepositoryを登録します。</summary>
public sealed class RepositoryRegistrationService(
    RepositoryAllowlist allowlist,
    LocalPathValidator pathValidator,
    IGitCommandClient gitCommandClient,
    IInteractiveApprovalPrompt approvalPrompt,
    IRepositoryConfigurationStore configurationStore) : IRepositoryRegistrationService
{
    private static readonly TimeSpan ApprovalTimeout = TimeSpan.FromMinutes(5);
    private const string DefaultRemote = "origin";
    private const string DefaultDevelopBranch = "develop";
    private const string DefaultMainBranch = "main";
    private const string DefaultTagPattern = "^v[0-9]+\\.[0-9]+\\.[0-9]+.*$";
    private readonly SemaphoreSlim _registrationLock = new(1, 1);

    public async Task<RepositoryRegistrationResult> RegisterAsync(
        RepositoryRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!RepositoryId.IsValid(request.Repository))
            return RepositoryRegistrationResult.Failure(RepositoryRegistrationError.InvalidRepositoryId);

        await _registrationLock.WaitAsync(cancellationToken);
        try
        {
            if (allowlist.TryGet(request.Repository, out _))
                return RepositoryRegistrationResult.Failure(RepositoryRegistrationError.DuplicateRepositoryId);

            var remote = string.IsNullOrWhiteSpace(request.Remote) ? DefaultRemote : request.Remote;
            var develop = string.IsNullOrWhiteSpace(request.DevelopBranch) ? DefaultDevelopBranch : request.DevelopBranch;
            var main = string.IsNullOrWhiteSpace(request.MainBranch) ? DefaultMainBranch : request.MainBranch;
            if (!IsValidGitName(remote) || !IsValidGitName(develop) || !IsValidGitName(main))
                return RepositoryRegistrationResult.Failure(RepositoryRegistrationError.InvalidRemote);

            RepositoryValidationResult pathResult;
            try
            {
                var provisional = CreateOptions(string.Empty, string.Empty, request.LocalRoot, remote, develop, main);
                pathResult = pathValidator.Validate(provisional);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return RepositoryRegistrationResult.Failure(RepositoryRegistrationError.InvalidLocalRoot);
            }
            if (!pathResult.IsAllowed)
                return RepositoryRegistrationResult.Failure(MapPathError(pathResult.Error!.Value));

            var localRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.LocalRoot));
            var remoteResult = await gitCommandClient.GetRemoteUrlAsync(localRoot, remote, cancellationToken);
            if (!remoteResult.IsSuccess)
                return RepositoryRegistrationResult.Failure(
                    remoteResult.Failure == GitCommandFailure.Failed
                        ? RepositoryRegistrationError.InvalidRemote
                        : RepositoryRegistrationError.GitFailed);

            var remoteUrl = remoteResult.StandardOutput.Trim();
            if (GitHubRemoteUrlValidator.IsSshRemote(remoteUrl))
                return RepositoryRegistrationResult.Failure(RepositoryRegistrationError.RemoteHttpsRequired);
            var parsed = GitHubRemoteUrlValidator.TryParse(remoteUrl);
            if (parsed is null)
                return RepositoryRegistrationResult.Failure(RepositoryRegistrationError.NonGitHubRemote);

            var options = CreateOptions(parsed.Value.Owner, parsed.Value.Repo, localRoot, remote, develop, main);
            var approval = await approvalPrompt.RequestApprovalAsync(
                new ApprovalPromptRequest(
                    "Githubie repository registration",
                    $"Register '{request.Repository}' for {parsed.Value.Owner}/{parsed.Value.Repo}",
                    [$"Local root: {localRoot}", $"Remote: {remote}", $"Branches: {develop} -> {main}"]),
                ApprovalTimeout,
                cancellationToken);
            var approvalError = MapApprovalError(approval.Outcome);
            if (approvalError is not null)
                return RepositoryRegistrationResult.Failure(approvalError.Value);

            try
            {
                await configurationStore.SaveRepositoryAsync(request.Repository, options, cancellationToken);
            }
            catch (IOException)
            {
                return RepositoryRegistrationResult.Failure(RepositoryRegistrationError.PersistenceFailed);
            }
            catch (UnauthorizedAccessException)
            {
                return RepositoryRegistrationResult.Failure(RepositoryRegistrationError.PersistenceFailed);
            }

            if (!allowlist.TryAdd(request.Repository, options))
                return RepositoryRegistrationResult.Failure(RepositoryRegistrationError.DuplicateRepositoryId);

            return RepositoryRegistrationResult.Success(new RepositoryRegistrationInfo(
                true, request.Repository, parsed.Value.Owner, parsed.Value.Repo, localRoot, remote, develop, main));
        }
        finally
        {
            _registrationLock.Release();
        }
    }

    private static RepositoryOptions CreateOptions(
        string owner, string repo, string localRoot, string remote, string develop, string main) => new(
        owner, repo, localRoot, remote, develop, main,
        [develop], [develop, main], [main], main, DefaultTagPattern, "merge", true);

    private static bool IsValidGitName(string value) =>
        !string.IsNullOrWhiteSpace(value) && value[0] != '-' &&
        value.IndexOfAny([' ', '\t', '\r', '\n']) < 0;

    private static RepositoryRegistrationError MapPathError(RepositoryValidationError error) => error switch
    {
        RepositoryValidationError.LocalRootNotFound => RepositoryRegistrationError.InvalidLocalRoot,
        RepositoryValidationError.GitMetadataNotFound => RepositoryRegistrationError.GitMetadataNotFound,
        RepositoryValidationError.ReparsePointDetected => RepositoryRegistrationError.ReparsePointDetected,
        _ => RepositoryRegistrationError.InvalidLocalRoot,
    };

    private static RepositoryRegistrationError? MapApprovalError(ApprovalOutcome outcome) => outcome switch
    {
        ApprovalOutcome.Approved => null,
        ApprovalOutcome.Denied => RepositoryRegistrationError.ApprovalDenied,
        ApprovalOutcome.TimedOut => RepositoryRegistrationError.ApprovalTimedOut,
        _ => RepositoryRegistrationError.ApprovalUnavailable,
    };
}
