using System.Diagnostics;
using System.Text.Json;
using Githubie.Application.Publishing;
using Microsoft.Extensions.Logging;

namespace Githubie.Server;

/// <summary>Includes both effect states in the audit without descriptions or transport responses.</summary>
public sealed class AuditedPrPublicationService(IPrPublicationService inner, IGithubieAuditLogger audit,
    ILogger<AuditedPrPublicationService>? logger = null) : IPrPublicationService
{
    public Task<PrPublicationResult> PublishAsync(string repository, PrPublishInput input, CancellationToken cancellationToken) =>
        RunAsync("github_pr_publish_create", repository, () => inner.PublishAsync(repository, input, cancellationToken));

    public Task<PrPublicationResult> GetStatusAsync(string repository, string operationId, CancellationToken cancellationToken) =>
        RunAsync("github_pr_publish_status", repository, () => inner.GetStatusAsync(repository, operationId, cancellationToken));

    private async Task<PrPublicationResult> RunAsync(string tool, string repository, Func<Task<PrPublicationResult>> action)
    {
        var watch = Stopwatch.StartNew();
        var result = await action();
        var info = result.Data;
        try
        {
            audit.Write(new GithubieAuditEvent("mcp", tool, Safe(repository), Safe(info.SourceBranch), info.PullRequestNumber,
                null, result.IsSuccess ? "success" : result.Outcome, watch.ElapsedMilliseconds, result.ErrorCode,
                CorrelationId: Guid.TryParse(info.OperationId, out var id) ? id.ToString("N") : null,
                Diagnostic: JsonSerializer.Serialize(new
                {
                    OperationId = Safe(info.OperationId), Destination = Safe(info.DestinationBranch),
                    Sha = Safe(info.SourceSha), info.Stage, info.PublicationState, info.PullRequestState,
                })));
            return result;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger?.LogWarning("Publication audit failed ({ExceptionType}).", exception.GetType().Name);
            // Never lose the completed/partial effect payload or retry an external mutation after audit failure.
            return result.IsSuccess ? new(result.Data, "audit_unavailable") : result;
        }
    }

    private static string Safe(string? value) => value is not null && value.Length <= 255 && !value.Any(char.IsControl) ? value : "invalid";
}

public sealed class PrPublicationDiagnostics(ILogger<PrPublicationDiagnostics> logger) : IPrPublicationDiagnostics
{
    public void Failure(string stage, string exceptionType) =>
        logger.LogWarning("Publication {Stage} failed ({ExceptionType}).", stage, exceptionType);
}
