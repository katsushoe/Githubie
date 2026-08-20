using System.Text.Json;

namespace Githubie.Application.Interactive;

public sealed record ApprovalPromptResponse(bool Approved);

public static class ApprovalPipeProtocol
{
    public const int MaxPayloadBytes = 16384;

    public static async Task WriteFrameAsync<T>(Stream target, T payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(payload);
        var json = JsonSerializer.SerializeToUtf8Bytes(payload);
        if (json.Length > MaxPayloadBytes)
        {
            throw new InvalidOperationException("Approval prompt payload exceeds the maximum allowed size.");
        }

        await target.WriteAsync(BitConverter.GetBytes(json.Length), cancellationToken);
        await target.WriteAsync(json, cancellationToken);
        await target.FlushAsync(cancellationToken);
    }

    public static async Task<T?> ReadFrameAsync<T>(Stream source, CancellationToken cancellationToken) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        var header = new byte[4];
        if (!await ReadExactAsync(source, header, cancellationToken)) return null;
        var length = BitConverter.ToInt32(header);
        if (length is < 0 or > MaxPayloadBytes) return null;
        var payload = new byte[length];
        if (!await ReadExactAsync(source, payload, cancellationToken)) return null;
        try { return JsonSerializer.Deserialize<T>(payload); }
        catch (JsonException) { return null; }
    }

    private static async Task<bool> ReadExactAsync(Stream source, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await source.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0) return false;
            offset += read;
        }
        return true;
    }
}
