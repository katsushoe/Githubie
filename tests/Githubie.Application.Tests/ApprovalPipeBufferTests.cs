using FluentAssertions;
using Githubie.Application.Interactive;
using Xunit;

namespace Githubie.Application.Tests;

public sealed class ApprovalPipeBufferTests
{
    [Fact]
    public async Task WriteAndRead_TokenResponse_ClearTransportBuffers()
    {
        using var stream = new ObservedStream();
        await ApprovalPipeProtocol.WriteFrameAsync(stream, new TokenPromptResponse(true, "test-secret"),
            TestContext.Current.CancellationToken);
        stream.WritePayload.ToArray().Should().NotBeEmpty().And.OnlyContain(value => value == 0);

        stream.Position = 0;
        var response = await ApprovalPipeProtocol.ReadFrameAsync<TokenPromptResponse>(stream,
            TestContext.Current.CancellationToken);

        response!.Token.Should().Be("test-secret");
        stream.ReadPayload.ToArray().Should().NotBeEmpty().And.OnlyContain(value => value == 0);
    }

    private sealed class ObservedStream : MemoryStream
    {
        public ReadOnlyMemory<byte> WritePayload { get; private set; }
        public Memory<byte> ReadPayload { get; private set; }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Length > 4) WritePayload = buffer;
            return base.WriteAsync(buffer, cancellationToken);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Length > 4) ReadPayload = buffer;
            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}
