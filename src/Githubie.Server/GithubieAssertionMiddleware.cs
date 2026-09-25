using System.Text.Json;
using Githubie.Application.Configuration;
using Githubie.Application.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moyai.ProviderAuthentication;

namespace Githubie.Server;

/// <summary>Repository ToolのHTTP要求を共通Provider Assertionで検証します。</summary>
public sealed class GithubieAssertionMiddleware(RequestDelegate next, ILogger<GithubieAssertionMiddleware> logger)
{
    private const int MaximumRequestBytes = 1_048_576;

    /// <summary>認証対象Toolだけを検証し、成功時に限りMCP処理へ渡します。</summary>
    public async Task InvokeAsync(
        HttpContext httpContext,
        GithubieOptions options,
        RepositoryAllowlist repositories,
        IAssertionValidator validator,
        IAssertionExecutionValidator executionValidator,
        GithubieAssertionExecutionContext executionContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (!httpContext.Request.Path.StartsWithSegments(options.McpPath))
        {
            await next(httpContext);
            return;
        }

        ToolCall? call = null;
        try
        {
            call = await ReadToolCallAsync(httpContext.Request, httpContext.RequestAborted);
            if (call is null || !GithubieAssertionPolicy.TryGetScopes(call.Tool, out var scopes))
            {
                await next(httpContext);
                return;
            }

            // Moyai由来の目印がない要求はローカル直接呼び出しとして扱う。目印がある要求は別経路へ落とさず厳密に検証する。
            if (!IsProviderRequest(httpContext.Request))
            {
                if (options.ProviderAuthentication is { RequireAssertion: true })
                {
                    throw new ProviderAuthenticationException("auth_assertion_missing");
                }

                await next(httpContext);
                return;
            }

            var expected = CreateExpectedContext(httpContext, call, scopes, options, repositories);
            var assertion = ReadAssertion(httpContext.Request);
            var principal = await validator.ValidateAsync(assertion, expected, httpContext.RequestAborted);
            using var executionScope = executionContext.Begin(principal, executionValidator);
            await executionContext.EnsureCurrentAsync(httpContext.RequestAborted);
            await next(httpContext);
        }
        catch (ProviderAuthenticationException exception)
        {
            logger.LogWarning(
                "[ProviderAuthentication] Request rejected for tool {Tool}: {Code}",
                call?.Tool ?? "unknown",
                exception.Code);
            Reject(httpContext.Response, exception.Code);
        }
    }

    private static AssertionContext CreateExpectedContext(
        HttpContext httpContext,
        ToolCall call,
        string[] scopes,
        GithubieOptions options,
        RepositoryAllowlist repositories)
    {
        if (string.IsNullOrWhiteSpace(call.Repository)
            || !repositories.TryGet(call.Repository, out var repository)
            || options.ProviderAuthentication is not { } authentication)
        {
            throw new ProviderAuthenticationException("auth_project_mismatch");
        }

        var mapping = authentication.Projects.FirstOrDefault(
            pair => string.Equals(pair.Key, call.Repository, StringComparison.OrdinalIgnoreCase));
        if (mapping.Key is null || mapping.Value == Guid.Empty)
        {
            throw new ProviderAuthenticationException("auth_project_mismatch");
        }

        var operationId = httpContext.Request.Headers["X-Moyai-Operation-Id"].ToString();
        if (string.IsNullOrWhiteSpace(operationId)
            || operationId.Length > 1024
            || operationId.Any(char.IsControl))
        {
            throw new ProviderAuthenticationException("auth_project_mismatch");
        }

        var repositoryId = $"github.com/{repository.GitHubOwner}/{repository.GitHubRepo}";
        return AssertionContext.ForRepository(
            GithubieAssertionPolicy.ProviderId,
            mapping.Value,
            repositoryId,
            call.Tool,
            scopes,
            operationId);
    }

    private static bool IsProviderRequest(HttpRequest request) =>
        request.Headers.Authorization.Count > 0 || request.Headers.ContainsKey("X-Moyai-Operation-Id");

    private static string ReadAssertion(HttpRequest request)
    {
        if (request.Headers.Authorization.Count != 1)
        {
            throw new ProviderAuthenticationException("auth_assertion_missing");
        }

        var authorization = request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.Ordinal)
            || authorization.Length == prefix.Length)
        {
            throw new ProviderAuthenticationException("auth_assertion_missing");
        }

        return authorization[prefix.Length..];
    }

    private static async Task<ToolCall?> ReadToolCallAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaximumRequestBytes)
        {
            throw new ProviderAuthenticationException("auth_assertion_invalid");
        }

        request.EnableBuffering(bufferThreshold: 65_536, bufferLimit: MaximumRequestBytes);
        try
        {
            using var document = await JsonDocument.ParseAsync(
                request.Body,
                new JsonDocumentOptions { MaxDepth = 32 },
                cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("method", out var method)
                || method.GetString() != "tools/call"
                || !root.TryGetProperty("params", out var parameters)
                || !parameters.TryGetProperty("name", out var name))
            {
                return null;
            }

            string tool = name.GetString() ?? string.Empty;
            string? repository = null;
            if (parameters.TryGetProperty("arguments", out var arguments)
                && arguments.ValueKind == JsonValueKind.Object
                && arguments.TryGetProperty("repository", out var repositoryElement))
            {
                repository = repositoryElement.GetString();
            }

            return new ToolCall(tool, repository);
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    private static void Reject(HttpResponse response, string code)
    {
        response.StatusCode = code switch
        {
            "authentication_unavailable" => StatusCodes.Status503ServiceUnavailable,
            "auth_audience_mismatch" or "auth_project_mismatch" or "auth_scope_denied"
                or "auth_protocol_unsupported" or "provider_capability_missing" => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status401Unauthorized,
        };
        if (response.StatusCode == StatusCodes.Status401Unauthorized)
        {
            response.Headers.WWWAuthenticate = "Bearer";
        }
    }

    private sealed record ToolCall(string Tool, string? Repository);
}
