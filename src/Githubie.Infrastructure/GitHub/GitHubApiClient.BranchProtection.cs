using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Githubie.Application.GitHub;

namespace Githubie.Infrastructure.GitHub;

public sealed partial class GitHubApiClient
{
    private const int MaxProtectionResponseBytes = 1024 * 1024;

    // これは未公開のtransport部品です。公開Gatewayは別途Desktop承認と監査を必須にします。
    internal async Task<GitHubResult<JsonObject>> ReadBranchProtectionAsync(
        string repositoryId, string owner, string repo, string branch, CancellationToken cancellationToken)
    {
        if (!GitHubProtectionBranchName.IsValid(branch)) return GitHubResult<JsonObject>.Failure(GitHubError.BranchNotAllowed);
        if (CredentialError(repositoryId, JsonValue.Create(branch)!) is { } inputError)
            return GitHubResult<JsonObject>.Failure(inputError);
        var response = await SendAsync(repositoryId, HttpMethod.Get, ProtectionPath(owner, repo, branch), null, cancellationToken,
            notFoundError: GitHubError.BranchNotFound);
        if (!response.IsSuccess)
            return GitHubResult<JsonObject>.Failure(response.Error ?? GitHubError.ApiError, httpStatus: response.HttpStatus);
        using var message = response.Value!;
        var body = await ReadProtectionObjectAsync(message, cancellationToken);
        if (body is null) return GitHubResult<JsonObject>.Failure(GitHubError.InvalidResponse);
        if (CredentialError(repositoryId, body) is { } responseError) return GitHubResult<JsonObject>.Failure(responseError);
        return GitHubResult<JsonObject>.Success(body);
    }

    internal async Task<GitHubResult<bool>> PutBranchProtectionAsync(
        string repositoryId, string owner, string repo, string branch, JsonObject approvedPayload, CancellationToken cancellationToken)
    {
        if (!GitHubProtectionBranchName.IsValid(branch)) return GitHubResult<bool>.Failure(GitHubError.BranchNotAllowed);
        ArgumentNullException.ThrowIfNull(approvedPayload);
        if (CredentialError(repositoryId, JsonValue.Create(branch)!) is { } inputError) return GitHubResult<bool>.Failure(inputError);
        if (CredentialError(repositoryId, approvedPayload) is { } payloadError) return GitHubResult<bool>.Failure(payloadError);
        try
        {
            using var content = new StringContent(approvedPayload.ToJsonString(), Encoding.UTF8, "application/json");
            var response = await SendAsync(repositoryId, HttpMethod.Put, ProtectionPath(owner, repo, branch), null, cancellationToken,
                rawContent: content, notFoundError: GitHubError.BranchNotFound);
            if (!response.IsSuccess) return MutationFailure(response);
            using var message = response.Value!;
            var body = await ReadProtectionObjectAsync(message, cancellationToken);
            if (body is null || !GitHubBranchProtectionPayload.TryNormalize(body, out var actual)
                || !JsonNode.DeepEquals(actual, approvedPayload))
                return GitHubResult<bool>.Failure(GitHubError.BranchProtectionResultUnknown);
            return GitHubResult<bool>.Success(true);
        }
        catch (OperationCanceledException)
        {
            // 送信前後のいずれで取り消されたかを外部境界では証明できません。
            return GitHubResult<bool>.Failure(GitHubError.BranchProtectionResultUnknown);
        }
    }

    internal async Task<GitHubResult<bool>> DeleteBranchProtectionAsync(
        string repositoryId, string owner, string repo, string branch, CancellationToken cancellationToken)
    {
        if (!GitHubProtectionBranchName.IsValid(branch)) return GitHubResult<bool>.Failure(GitHubError.BranchNotAllowed);
        if (CredentialError(repositoryId, JsonValue.Create(branch)!) is { } inputError) return GitHubResult<bool>.Failure(inputError);
        try
        {
            var response = await SendAsync(repositoryId, HttpMethod.Delete, ProtectionPath(owner, repo, branch), null, cancellationToken,
                notFoundError: GitHubError.BranchNotFound);
            if (!response.IsSuccess) return MutationFailure(response);
            using var message = response.Value!;
            return message.StatusCode == HttpStatusCode.NoContent
                ? GitHubResult<bool>.Success(true)
                : GitHubResult<bool>.Failure(GitHubError.BranchProtectionResultUnknown);
        }
        catch (OperationCanceledException)
        {
            return GitHubResult<bool>.Failure(GitHubError.BranchProtectionResultUnknown);
        }
    }

    private static string ProtectionPath(string owner, string repo, string branch) =>
        $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/branches/{Uri.EscapeDataString(branch)}/protection";

    private GitHubError? CredentialError(string repositoryId, JsonNode node)
    {
        var read = _tokenStore.Read(repositoryId);
        if (!read.IsSuccess || read.Token is null) return GitHubError.AuthenticationFailed;
        try
        {
            if (read.Token.Length == 0) return GitHubError.AuthenticationFailed;
            return ContainsCredential(node, read.Token) ? GitHubError.InvalidResponse : null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(read.Token.AsSpan()));
        }
    }

    private static bool ContainsCredential(JsonNode? node, ReadOnlySpan<char> credential)
    {
        if (node is JsonObject obj)
        {
            foreach (var item in obj)
                if (item.Key.AsSpan().Contains(credential, StringComparison.Ordinal) || ContainsCredential(item.Value, credential)) return true;
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
                if (ContainsCredential(item, credential)) return true;
        }
        else if (node is JsonValue value && value.TryGetValue<string>(out var text) && text is not null)
        {
            return text.AsSpan().Contains(credential, StringComparison.Ordinal);
        }
        return false;
    }

    private static GitHubResult<bool> MutationFailure(GitHubResult<HttpResponseMessage> response)
    {
        var uncertain = response.Error is GitHubError.NetworkError or GitHubError.Timeout
            || response.HttpStatus is 408 or >= 500;
        return GitHubResult<bool>.Failure(uncertain ? GitHubError.BranchProtectionResultUnknown : response.Error ?? GitHubError.ApiError,
            httpStatus: response.HttpStatus);
    }

    private static async Task<JsonObject?> ReadProtectionObjectAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            if (response.Content.Headers.ContentLength is > MaxProtectionResponseBytes) return null;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (output.Length + read > MaxProtectionResponseBytes) return null;
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            output.Position = 0;
            return await JsonNode.ParseAsync(output, cancellationToken: cancellationToken) as JsonObject;
        }
        catch (Exception exception) when (exception is JsonException or IOException or HttpRequestException or InvalidOperationException)
        {
            return null;
        }
    }
}
