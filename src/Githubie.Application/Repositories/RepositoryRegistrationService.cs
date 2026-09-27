using Githubie.Application.Configuration;
using Githubie.Application.Credentials;
using System.Text.RegularExpressions;
using Githubie.Application.Git;
using Githubie.Application.GitHub;
using Githubie.Application.Interactive;

namespace Githubie.Application.Repositories;

/// <summary>Git remote由来の情報だけを用いてRepositoryを登録し、対話承認付きでGitHub Repositoryを新規作成します。</summary>
public sealed partial class RepositoryRegistrationService(
    RepositoryAllowlist allowlist,
    LocalPathValidator pathValidator,
    IGitCommandClient gitCommandClient,
    IInteractiveApprovalPrompt approvalPrompt,
    IRepositoryConfigurationStore configurationStore,
    IInteractiveTokenPrompt tokenPrompt,
    IApiTokenStore tokenStore,
    IGitHubApiClient gitHubApiClient) : IRepositoryRegistrationService
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
        if (!RepositoryId.TryNormalize(request.Repository, out var repositoryId))
            return RepositoryRegistrationResult.Failure(RepositoryRegistrationError.InvalidRepositoryId);
        if ((request.CommitAuthorName is null) != (request.CommitAuthorEmail is null)
            || (request.CommitAuthorName is not null && !CommitAuthorIdentity.IsValid(request.CommitAuthorName, request.CommitAuthorEmail)))
            return RepositoryRegistrationResult.Failure(RepositoryRegistrationError.InvalidAuthorIdentity);

        await _registrationLock.WaitAsync(cancellationToken);
        try
        {
            if (allowlist.TryGet(repositoryId, out _))
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

            var authorName = request.CommitAuthorName;
            var authorEmail = request.CommitAuthorEmail;
            if (authorName is null)
            {
                var localName = await gitCommandClient.GetLocalConfigAsync(localRoot, "user.name", cancellationToken);
                var localEmail = await gitCommandClient.GetLocalConfigAsync(localRoot, "user.email", cancellationToken);
                if (localName.IsSuccess && localEmail.IsSuccess
                    && CommitAuthorIdentity.IsValid(localName.StandardOutput.Trim(), localEmail.StandardOutput.Trim()))
                {
                    authorName = localName.StandardOutput.Trim();
                    authorEmail = localEmail.StandardOutput.Trim();
                }
            }

            var options = CreateOptions(parsed.Value.Owner, parsed.Value.Repo, localRoot, remote, develop, main) with
            {
                CommitAuthorName = authorName,
                CommitAuthorEmail = authorEmail,
            };
            var approval = await approvalPrompt.RequestApprovalAsync(
                new ApprovalPromptRequest(
                    "Githubie repository registration",
                    $"Register '{repositoryId}' for {parsed.Value.Owner}/{parsed.Value.Repo}",
                    [$"Local root: {localRoot}", $"Remote: {remote}", $"Branches: {develop} -> {main}",
                     $"Commit author: {authorName ?? "not configured"} <{authorEmail ?? "not configured"}>"]),
                ApprovalTimeout,
                cancellationToken);
            var approvalError = MapApprovalError(approval.Outcome);
            if (approvalError is not null)
                return RepositoryRegistrationResult.Failure(approvalError.Value);

            try
            {
                await configurationStore.SaveRepositoryAsync(repositoryId, options, cancellationToken);
            }
            catch (IOException)
            {
                return RepositoryRegistrationResult.Failure(RepositoryRegistrationError.PersistenceFailed);
            }
            catch (UnauthorizedAccessException)
            {
                return RepositoryRegistrationResult.Failure(RepositoryRegistrationError.PersistenceFailed);
            }

            if (!allowlist.TryAdd(repositoryId, options))
                return RepositoryRegistrationResult.Failure(RepositoryRegistrationError.DuplicateRepositoryId);

            var tokenResult = await ConfigureTokenAsync(
                repositoryId,
                $"https://github.com/{parsed.Value.Owner}/{parsed.Value.Repo}",
                cancellationToken);
            return RepositoryRegistrationResult.Success(new RepositoryRegistrationInfo(
                true, repositoryId, parsed.Value.Owner, parsed.Value.Repo, localRoot, remote, develop, main,
                tokenResult.Configured, tokenResult.Status));
        }
        finally
        {
            _registrationLock.Release();
        }
    }

    public async Task<RepositoryCreateResult> CreateAsync(
        RepositoryCreateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!RepositoryId.TryNormalize(request.Repository, out var repositoryId))
            return RepositoryCreateResult.Failure(RepositoryRegistrationError.InvalidRepositoryId);
        if (!GitHubOwnerPattern().IsMatch(request.Owner ?? string.Empty)
            || !GitHubRepositoryNamePattern().IsMatch(request.Name ?? string.Empty)
            || request.Name is "." or "..")
            return RepositoryCreateResult.Failure(RepositoryRegistrationError.InvalidGitHubName);
        bool? isPrivate = request.Visibility?.Trim().ToLowerInvariant() switch
        {
            null or "" or "private" => true,
            "public" => false,
            _ => null,
        };
        if (isPrivate is null)
            return RepositoryCreateResult.Failure(RepositoryRegistrationError.InvalidVisibility);
        if ((request.CommitAuthorName is null) != (request.CommitAuthorEmail is null)
            || (request.CommitAuthorName is not null && !CommitAuthorIdentity.IsValid(request.CommitAuthorName, request.CommitAuthorEmail)))
            return RepositoryCreateResult.Failure(RepositoryRegistrationError.InvalidAuthorIdentity);

        var develop = string.IsNullOrWhiteSpace(request.DevelopBranch) ? DefaultDevelopBranch : request.DevelopBranch;
        var main = string.IsNullOrWhiteSpace(request.MainBranch) ? DefaultMainBranch : request.MainBranch;
        if (!IsValidGitName(develop) || !IsValidGitName(main))
            return RepositoryCreateResult.Failure(RepositoryRegistrationError.InvalidRemote);

        await _registrationLock.WaitAsync(cancellationToken);
        try
        {
            if (allowlist.TryGet(repositoryId, out _))
                return RepositoryCreateResult.Failure(RepositoryRegistrationError.DuplicateRepositoryId);

            string? localRoot = null;
            if (!string.IsNullOrWhiteSpace(request.LocalRoot))
            {
                RepositoryValidationResult pathResult;
                try
                {
                    pathResult = pathValidator.Validate(
                        CreateOptions(string.Empty, string.Empty, request.LocalRoot, DefaultRemote, develop, main));
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    return RepositoryCreateResult.Failure(RepositoryRegistrationError.InvalidLocalRoot);
                }
                if (!pathResult.IsAllowed)
                    return RepositoryCreateResult.Failure(MapPathError(pathResult.Error!.Value));

                localRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.LocalRoot));
                // 既存remoteは書き換えない。未設定（remote get-urlの通常失敗）の場合だけ作成先を設定する。
                var existing = await gitCommandClient.GetRemoteUrlAsync(localRoot, DefaultRemote, cancellationToken);
                if (existing.IsSuccess)
                    return RepositoryCreateResult.Failure(RepositoryRegistrationError.RemoteAlreadyConfigured);
                if (existing.Failure != GitCommandFailure.Failed)
                    return RepositoryCreateResult.Failure(RepositoryRegistrationError.GitFailed);
            }

            var visibility = isPrivate.Value ? "private" : "public";
            var approval = await approvalPrompt.RequestApprovalAsync(
                new ApprovalPromptRequest(
                    "Githubie repository creation",
                    $"Create {visibility} GitHub repository {request.Owner}/{request.Name}",
                    [$"Githubie repository ID: {repositoryId}",
                     $"Description: {request.Description ?? "(none)"}",
                     localRoot is null
                        ? "Local root: not linked (the repository is created on GitHub only)"
                        : $"Local root: {localRoot} (sets remote '{DefaultRemote}' and registers it)",
                     $"Branches: {develop} -> {main}"]),
                ApprovalTimeout,
                cancellationToken);
            var approvalError = MapApprovalError(approval.Outcome);
            if (approvalError is not null)
                return RepositoryCreateResult.Failure(approvalError.Value);

            var token = await EnsureTokenAsync(repositoryId, $"https://github.com/{request.Owner}/{request.Name}", cancellationToken);
            if (token.Status is null)
                return RepositoryCreateResult.Failure(RepositoryRegistrationError.TokenUnavailable);

            var login = await gitHubApiClient.GetAuthenticatedUserLoginAsync(repositoryId, cancellationToken);
            if (!login.IsSuccess)
            {
                DiscardNewToken(repositoryId, token.SavedNow);
                return RepositoryCreateResult.Failure(MapGitHubCreateError(login.Error!.Value), login.Diagnostic);
            }

            var created = await gitHubApiClient.CreateRepositoryAsync(
                repositoryId,
                new GitHubRepositoryCreate(
                    request.Owner!, request.Name!, isPrivate.Value, request.Description,
                    ForOrganization: !string.Equals(login.Value, request.Owner, StringComparison.OrdinalIgnoreCase)),
                cancellationToken);
            if (!created.IsSuccess)
            {
                DiscardNewToken(repositoryId, token.SavedNow);
                return RepositoryCreateResult.Failure(MapGitHubCreateError(created.Error!.Value), created.Diagnostic);
            }

            var repository = created.Value!;
            if (localRoot is null)
            {
                return RepositoryCreateResult.Success(new RepositoryCreateInfo(
                    repositoryId, repository.Owner, repository.Repo, repository.Private, repository.HtmlUrl,
                    false, null, null, token.Status));
            }

            var remoteUrl = $"https://github.com/{repository.Owner}/{repository.Repo}.git";
            var added = await gitCommandClient.AddRemoteAsync(localRoot, DefaultRemote, remoteUrl, cancellationToken);
            if (!added.IsSuccess)
            {
                return RepositoryCreateResult.Failure(
                    RepositoryRegistrationError.GitFailed,
                    $"GitHub repository {repository.HtmlUrl} was created, but remote '{DefaultRemote}' could not be set in {localRoot}.");
            }

            var (authorName, authorEmail) = await ResolveAuthorAsync(
                localRoot, request.CommitAuthorName, request.CommitAuthorEmail, cancellationToken);
            var options = CreateOptions(repository.Owner, repository.Repo, localRoot, DefaultRemote, develop, main) with
            {
                CommitAuthorName = authorName,
                CommitAuthorEmail = authorEmail,
            };
            try
            {
                await configurationStore.SaveRepositoryAsync(repositoryId, options, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return RepositoryCreateResult.Failure(
                    RepositoryRegistrationError.PersistenceFailed,
                    $"GitHub repository {repository.HtmlUrl} was created and remote '{DefaultRemote}' was set, but the registration could not be saved.");
            }

            if (!allowlist.TryAdd(repositoryId, options))
                return RepositoryCreateResult.Failure(RepositoryRegistrationError.DuplicateRepositoryId);

            return RepositoryCreateResult.Success(new RepositoryCreateInfo(
                repositoryId, repository.Owner, repository.Repo, repository.Private, repository.HtmlUrl,
                true, localRoot, DefaultRemote, token.Status));
        }
        finally
        {
            _registrationLock.Release();
        }
    }

    /// <summary>保存済みTokenがあれば使い、なければ入力画面で受け取って保存します。</summary>
    private async Task<(string? Status, bool SavedNow)> EnsureTokenAsync(
        string repositoryId, string repositoryUrl, CancellationToken cancellationToken)
    {
        var stored = tokenStore.Read(repositoryId);
        if (stored.IsSuccess && stored.Token is { Length: > 0 })
        {
            Array.Clear(stored.Token);
            return ("existing", false);
        }

        var (configured, status) = await ConfigureTokenAsync(repositoryId, repositoryUrl, cancellationToken);
        return configured ? (status, true) : (null, false);
    }

    /// <summary>作成に失敗した場合、この呼び出しで保存したTokenだけを削除します。</summary>
    private void DiscardNewToken(string repositoryId, bool savedNow)
    {
        if (savedNow)
        {
            tokenStore.Delete(repositoryId);
        }
    }

    private async Task<(string? Name, string? Email)> ResolveAuthorAsync(
        string localRoot, string? name, string? email, CancellationToken cancellationToken)
    {
        if (name is not null)
        {
            return (name, email);
        }

        var localName = await gitCommandClient.GetLocalConfigAsync(localRoot, "user.name", cancellationToken);
        var localEmail = await gitCommandClient.GetLocalConfigAsync(localRoot, "user.email", cancellationToken);
        return localName.IsSuccess && localEmail.IsSuccess
            && CommitAuthorIdentity.IsValid(localName.StandardOutput.Trim(), localEmail.StandardOutput.Trim())
            ? (localName.StandardOutput.Trim(), localEmail.StandardOutput.Trim())
            : (null, null);
    }

    private static RepositoryRegistrationError MapGitHubCreateError(GitHubError error) => error switch
    {
        GitHubError.RepositoryAlreadyExists => RepositoryRegistrationError.GitHubRepositoryAlreadyExists,
        GitHubError.AuthenticationFailed => RepositoryRegistrationError.GitHubAuthenticationFailed,
        GitHubError.PermissionDenied or GitHubError.TokenScopeMissing => RepositoryRegistrationError.GitHubPermissionDenied,
        _ => RepositoryRegistrationError.GitHubFailed,
    };

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$")]
    private static partial Regex GitHubOwnerPattern();

    [GeneratedRegex("^[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex GitHubRepositoryNamePattern();

    private async Task<(bool Configured, string Status)> ConfigureTokenAsync(
        string repositoryId,
        string repositoryUrl,
        CancellationToken cancellationToken)
    {
        var promptResult = await tokenPrompt.RequestTokenAsync(
            new TokenPromptRequest(repositoryId, repositoryUrl), cancellationToken);
        if (promptResult.Outcome != InteractiveTokenPromptOutcome.Accepted || promptResult.Token is null)
        {
            return (false, promptResult.Outcome == InteractiveTokenPromptOutcome.Skipped
                ? "skipped"
                : "prompt_unavailable");
        }

        try
        {
            var saveResult = tokenStore.Save(repositoryId, promptResult.Token);
            return saveResult.IsSuccess ? (true, "saved") : (false, "save_failed");
        }
        finally
        {
            Array.Clear(promptResult.Token);
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
