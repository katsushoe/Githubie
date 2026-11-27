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
    IGitHubApiClient gitHubApiClient,
    MoyaiProjectMapping? moyaiProjects = null,
    IAtomicRepositoryRegistrationStore? atomicRegistrationStore = null,
    RepositoryMutationLock? mutationLock = null) : IRepositoryRegistrationService
{
    private readonly MoyaiProjectMapping _moyaiProjects = moyaiProjects ?? new MoyaiProjectMapping(allowlist, null);

    private static readonly TimeSpan ApprovalTimeout = TimeSpan.FromMinutes(5);
    private const string DefaultRemote = "origin";
    private const string DefaultDevelopBranch = "develop";
    private const string DefaultMainBranch = "main";
    private const string DefaultTagPattern = "^v[0-9]+\\.[0-9]+\\.[0-9]+.*$";
    private readonly SemaphoreSlim _registrationLock = mutationLock?.Gate ?? new(1, 1);

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
            if (request.MoyaiProjectId is { } requestedProject
                && _moyaiProjects.CheckAssignable(repositoryId, requestedProject) is { } mappingError)
                return RepositoryRegistrationResult.Failure(mappingError == MoyaiProjectMappingError.InvalidProjectId
                    ? RepositoryRegistrationError.InvalidMoyaiProjectId
                    : RepositoryRegistrationError.DuplicateMoyaiProjectId);

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
                MoyaiProjectId = request.MoyaiProjectId,
            };
            var approval = await approvalPrompt.RequestApprovalAsync(
                new ApprovalPromptRequest(
                    "Githubie repository registration",
                    $"Register '{repositoryId}' for {parsed.Value.Owner}/{parsed.Value.Repo}",
                    [$"Local root: {localRoot}", $"Remote: {remote}", $"Branches: {develop} -> {main}",
                     $"Commit author: {authorName ?? "not configured"} <{authorEmail ?? "not configured"}>",
                     $"Moyai Project: {request.MoyaiProjectId?.ToString() ?? "not mapped"}"]),
                ApprovalTimeout,
                cancellationToken);
            var approvalError = MapApprovalError(approval.Outcome);
            if (approvalError is not null)
                return RepositoryRegistrationResult.Failure(approvalError.Value);

            if (atomicRegistrationStore is not null)
            {
                var credential = await tokenPrompt.RequestTokenAsync(new TokenPromptRequest(repositoryId,
                    $"https://github.com/{parsed.Value.Owner}/{parsed.Value.Repo}") { AllowSkip = true }, cancellationToken);
                try
                {
                    if (credential.Outcome != InteractiveTokenPromptOutcome.Skipped
                        && (credential.Outcome != InteractiveTokenPromptOutcome.Accepted || credential.Token is null))
                        return RepositoryRegistrationResult.Failure(RepositoryRegistrationError.TokenUnavailable);
                    await atomicRegistrationStore.SaveRegistrationAsync(repositoryId, options,
                        credential.Outcome == InteractiveTokenPromptOutcome.Accepted ? credential.Token : null, cancellationToken);
                    if (!allowlist.TryAdd(repositoryId, options))
                        throw new InvalidOperationException("Committed registration could not be published to the allowlist.");
                    return RepositoryRegistrationResult.Success(new RepositoryRegistrationInfo(
                        true, repositoryId, parsed.Value.Owner, parsed.Value.Repo, localRoot, remote, develop, main,
                        credential.Outcome == InteractiveTokenPromptOutcome.Accepted,
                        credential.Outcome == InteractiveTokenPromptOutcome.Accepted ? "saved" : "skipped")
                    { MoyaiProjectId = request.MoyaiProjectId });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return RepositoryRegistrationResult.Failure(RepositoryRegistrationError.PersistenceFailed);
                }
                finally { if (credential.Token is not null) Array.Clear(credential.Token); }
            }

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
                tokenResult.Configured, tokenResult.Status)
            {
                MoyaiProjectId = request.MoyaiProjectId,
            });
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
            char[]? registrationToken = null;
            try
            {
                if (atomicRegistrationStore is null)
                    await configurationStore.SaveRepositoryAsync(repositoryId, options, cancellationToken);
                else
                {
                    var stored = tokenStore.Read(repositoryId);
                    if (!stored.IsSuccess || stored.Token is not { Length: > 0 })
                        throw new IOException("The creation credential could not be read for registration.");
                    registrationToken = stored.Token;
                    await atomicRegistrationStore.SaveRegistrationAsync(repositoryId, options, registrationToken, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return RepositoryCreateResult.Failure(
                    RepositoryRegistrationError.PersistenceFailed,
                    $"GitHub repository {repository.HtmlUrl} was created and remote '{DefaultRemote}' was set, but the registration could not be saved.");
            }
            finally { if (registrationToken is not null) Array.Clear(registrationToken); }

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

    public async Task<RepositoryRelocateResult> RelocateAsync(
        string repository,
        string localRoot,
        CancellationToken cancellationToken)
    {
        if (!RepositoryId.TryNormalize(repository, out var repositoryId))
            return RepositoryRelocateResult.Failure(RepositoryRegistrationError.InvalidRepositoryId);
        if (string.IsNullOrWhiteSpace(localRoot))
            return RepositoryRelocateResult.Failure(RepositoryRegistrationError.InvalidLocalRoot);

        await _registrationLock.WaitAsync(cancellationToken);
        try
        {
            if (!allowlist.TryGet(repositoryId, out var existing))
                return RepositoryRelocateResult.Failure(RepositoryRegistrationError.RepositoryNotRegistered);

            RepositoryValidationResult pathResult;
            string newRoot;
            try
            {
                pathResult = pathValidator.Validate(existing with { LocalRoot = localRoot });
                newRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(localRoot));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return RepositoryRelocateResult.Failure(RepositoryRegistrationError.InvalidLocalRoot);
            }
            if (!pathResult.IsAllowed)
                return RepositoryRelocateResult.Failure(MapPathError(pathResult.Error!.Value));

            var remoteError = await VerifyRelocatedRemoteAsync(existing, newRoot, cancellationToken);
            if (remoteError is not null) return RepositoryRelocateResult.Failure(remoteError.Value);

            var previousRoot = existing.LocalRoot;
            if (string.Equals(previousRoot, newRoot, StringComparison.Ordinal))
                return RepositoryRelocateResult.Success(new(true, repositoryId, previousRoot, newRoot, existing.Remote));

            var approval = await approvalPrompt.RequestApprovalAsync(
                new ApprovalPromptRequest(
                    "Githubie repository relocation",
                    $"Change the local root of '{repositoryId}' ({existing.GitHubOwner}/{existing.GitHubRepo})",
                    [$"Local root: {previousRoot} -> {newRoot}",
                     $"Remote: {(string.IsNullOrWhiteSpace(existing.Remote) ? "resolved by repository URL" : existing.Remote)}",
                     "Token, branch policy, commit author and Moyai mapping are kept."]),
                ApprovalTimeout,
                cancellationToken);
            var approvalError = MapApprovalError(approval.Outcome);
            if (approvalError is not null) return RepositoryRelocateResult.Failure(approvalError.Value);

            var updated = existing with { LocalRoot = newRoot };
            try { await configurationStore.SaveRepositoryAsync(repositoryId, updated, cancellationToken); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { return RepositoryRelocateResult.Failure(RepositoryRegistrationError.PersistenceFailed); }
            if (!allowlist.TryUpdate(repositoryId, updated))
                return RepositoryRelocateResult.Failure(RepositoryRegistrationError.RepositoryNotRegistered);
            return RepositoryRelocateResult.Success(new(true, repositoryId, previousRoot, newRoot, existing.Remote));
        }
        finally
        {
            _registrationLock.Release();
        }
    }

    /// <summary>新しいLocal Rootのremoteが登録済みのGitHub Repositoryを指すことを確認します。</summary>
    private async Task<RepositoryRegistrationError?> VerifyRelocatedRemoteAsync(
        RepositoryOptions existing, string newRoot, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(existing.Remote))
        {
            if (!IsValidGitName(existing.Remote)) return RepositoryRegistrationError.InvalidRemote;
            var remoteResult = await gitCommandClient.GetRemoteUrlAsync(newRoot, existing.Remote, cancellationToken);
            if (!remoteResult.IsSuccess)
                return remoteResult.Failure == GitCommandFailure.Failed
                    ? RepositoryRegistrationError.InvalidRemote
                    : RepositoryRegistrationError.GitFailed;
            var url = remoteResult.StandardOutput.Trim();
            if (GitHubRemoteUrlValidator.IsSshRemote(url)) return RepositoryRegistrationError.RemoteHttpsRequired;
            return GitHubRemoteUrlValidator.IsExpectedRemote(url, existing.GitHubOwner, existing.GitHubRepo)
                ? null
                : RepositoryRegistrationError.RemoteMismatch;
        }

        var listed = await gitCommandClient.ListRemoteUrlsAsync(newRoot, cancellationToken);
        if (!listed.IsSuccess) return RepositoryRegistrationError.GitFailed;
        foreach (var line in listed.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf(' ');
            if (separator <= 0) continue;
            var url = line[(separator + 1)..].Trim();
            if (url.Length > 0 && !GitHubRemoteUrlValidator.IsSshRemote(url)
                && GitHubRemoteUrlValidator.IsExpectedRemote(url, existing.GitHubOwner, existing.GitHubRepo))
                return null;
        }
        return RepositoryRegistrationError.RemoteMismatch;
    }

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
