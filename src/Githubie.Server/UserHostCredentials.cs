using System.Security.Cryptography;
using Githubie.Application.Credentials;
using Githubie.Application.GitHub;
using Githubie.Application.Interactive;
using Githubie.Application.Repositories;
using Githubie.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace Githubie.Server;

/// <summary>認証済みユーザー制御Pipeからの資格操作です。秘密値を応答やログに含めません。</summary>
internal sealed class UserHostCredentials(RepositoryAllowlist repositories, IInteractiveTokenPrompt prompt,
    IApiTokenStore credentials, IGitHubRepositoryGateway gitHub, ILogger<UserHostCredentials> logger,
    RepositoryMutationLock? mutationLock = null)
{
    private readonly RepositoryMutationLock _mutations = mutationLock ?? new RepositoryMutationLock();

    public async Task<UserHostControlResponse> ExecuteAsync(UserHostControlRequest request, CancellationToken cancellationToken)
    {
        using var mutation = await _mutations.AcquireAsync(cancellationToken);
        if (request.Repository is null || !RepositoryId.TryNormalize(request.Repository, out var repositoryId)
            || !repositories.TryGet(repositoryId, out var repository)) return new(false, "repository_not_found");
        if (request.Command == "auth_test")
        {
            var result = await gitHub.GetRepositoryAsync(repositoryId, cancellationToken);
            return Result(result.IsSuccess, result.IsSuccess ? "authenticated" : "AUTH_FAILED");
        }
        if (request.Command == "auth_delete")
        {
            var deleted = credentials.Delete(repositoryId);
            return Result(deleted.IsSuccess, deleted.IsSuccess ? "deleted" : "DELETE_FAILED");
        }
        if (request.Command != "auth_set") return new(false, "unknown_command");
        InteractiveTokenPromptResult response;
        try
        {
            response = await prompt.RequestTokenAsync(new TokenPromptRequest(repositoryId,
                $"https://github.com/{repository.GitHubOwner}/{repository.GitHubRepo}"), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning("Token dialog failed with {ExceptionType}.", exception.GetType().Name);
            return Result(false, "DIALOG_FAILED");
        }
        try
        {
            if (response.Outcome != InteractiveTokenPromptOutcome.Accepted || response.Token is null)
                return Result(false, response.Outcome is InteractiveTokenPromptOutcome.Cancelled or InteractiveTokenPromptOutcome.Skipped
                    ? "CANCELLED" : "DIALOG_FAILED");
            var saved = credentials.Save(repositoryId, response.Token);
            return Result(saved.IsSuccess, saved.IsSuccess ? "saved" : "SAVE_FAILED");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            logger.LogWarning("Token persistence failed with {ExceptionType}.", exception.GetType().Name);
            return Result(false, "SAVE_FAILED");
        }
        finally { if (response.Token is not null) Array.Clear(response.Token); }

        UserHostControlResponse Result(bool success, string code)
        {
            logger.LogInformation("User credential operation {Command} for {Repository}: {Code}.", request.Command, repositoryId, code);
            return new(success, code);
        }
    }
}
