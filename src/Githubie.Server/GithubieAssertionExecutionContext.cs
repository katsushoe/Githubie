using System.Threading;
using Githubie.Application.Git;
using Moyai.ProviderAuthentication;

namespace Githubie.Server;

/// <summary>検証済みPrincipalを現在の非同期MCP要求へ限定して保持します。</summary>
public sealed class GithubieAssertionExecutionContext : IProviderExecutionGuard
{
    private readonly AsyncLocal<Entry?> _current = new();

    /// <summary>Principalを現在の要求へ設定し、破棄時に以前の状態へ戻します。</summary>
    public IDisposable Begin(AssertionPrincipal principal, IAssertionExecutionValidator validator)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(validator);
        var previous = _current.Value;
        _current.Value = new Entry(principal, validator);
        return new Scope(this, previous);
    }

    /// <inheritdoc />
    public Task EnsureCurrentAsync(CancellationToken cancellationToken = default) =>
        _current.Value is { } entry
            ? entry.Validator.EnsureCurrentAsync(entry.Principal, cancellationToken)
            : Task.CompletedTask;

    private sealed record Entry(AssertionPrincipal Principal, IAssertionExecutionValidator Validator);

    private sealed class Scope(GithubieAssertionExecutionContext owner, Entry? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            owner._current.Value = previous;
            _disposed = true;
        }
    }
}
