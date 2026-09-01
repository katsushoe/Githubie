using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Githubie.Server;

/// <summary>Windows Serviceの起動準備状態をDatabase外へ原子的に保存します。</summary>
public sealed class ServiceReadinessStore(string statePath)
{
    private const string InitializingStatus = "initializing";
    private const string ReadyStatus = "ready";
    private const string FailedStatus = "failed";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly string _statePath = Path.GetFullPath(statePath);

    /// <summary>現在のProcessを初期化中として記録します。</summary>
    public Task WriteInitializingAsync(CancellationToken cancellationToken) =>
        WriteAsync(CreateState(InitializingStatus), cancellationToken);

    /// <summary>現在のProcessを準備完了として記録します。</summary>
    public Task WriteReadyAsync(CancellationToken cancellationToken) =>
        WriteAsync(CreateState(ReadyStatus), cancellationToken);

    /// <summary>現在のProcessの起動失敗を記録します。</summary>
    public Task WriteFailedAsync(string error, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return WriteAsync(CreateState(FailedStatus) with { Error = error }, cancellationToken);
    }

    /// <summary>Serviceが準備完了になるまで、指定時間を上限として待機します。</summary>
    public async Task<ServiceReadinessResult> WaitForReadyAsync(
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (pollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await ReadAsync(cancellationToken);
            if (state is not null)
            {
                if (!IsMatchingProcess(state))
                    return ServiceReadinessResult.Failure("service process is not running");
                if (string.Equals(state.Status, ReadyStatus, StringComparison.Ordinal))
                    return ServiceReadinessResult.Success();
                if (string.Equals(state.Status, FailedStatus, StringComparison.Ordinal))
                    return ServiceReadinessResult.Failure(state.Error ?? "service initialization failed");
                if (!string.Equals(state.Status, InitializingStatus, StringComparison.Ordinal))
                    return ServiceReadinessResult.Failure($"unknown service state: {state.Status}");
            }

            await Task.Delay(pollInterval, cancellationToken);
        }

        return ServiceReadinessResult.Failure("service readiness timed out");
    }

    private static ServiceReadinessState CreateState(string status)
    {
        using var process = Process.GetCurrentProcess();
        return new ServiceReadinessState(
            status,
            process.Id,
            process.ProcessName,
            new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero),
            null);
    }

    private async Task WriteAsync(ServiceReadinessState state, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_statePath)
            ?? throw new IOException("Service state directory could not be resolved.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_statePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(state, SerializerOptions),
                cancellationToken);
            File.Move(temporaryPath, _statePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private async Task<ServiceReadinessState?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_statePath)) return null;
        await using var stream = File.OpenRead(_statePath);
        return await JsonSerializer.DeserializeAsync<ServiceReadinessState>(stream, SerializerOptions, cancellationToken)
            ?? throw new InvalidDataException("Service state was empty.");
    }

    private static bool IsMatchingProcess(ServiceReadinessState state)
    {
        try
        {
            using var process = Process.GetProcessById(state.ProcessId);
            return string.Equals(process.ProcessName, state.ProcessName, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}

/// <summary>保存されたService起動準備状態です。</summary>
public sealed record ServiceReadinessState(
    string Status,
    int ProcessId,
    string ProcessName,
    DateTimeOffset StartedAtUtc,
    string? Error);

/// <summary>Service準備完了待機の結果です。</summary>
public sealed record ServiceReadinessResult(bool IsReady, string? Error)
{
    public static ServiceReadinessResult Success() => new(true, null);

    public static ServiceReadinessResult Failure(string error) => new(false, error);
}
