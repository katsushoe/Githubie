using System.Diagnostics;
using System.IO.Pipes;
using Githubie.Application.Interactive;

namespace Githubie.Cli;

internal sealed class TokenPromptClient(string executablePath)
{
    public async Task<char[]?> RequestAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(executablePath)) return null;

        var pipeName = $"githubie-token-{Guid.NewGuid():N}";
        await using var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.In,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--token");
        startInfo.ArgumentList.Add(pipeName);

        using var process = Process.Start(startInfo);
        if (process is null) return null;
        try
        {
            await pipe.WaitForConnectionAsync(cancellationToken);
            var response = await ApprovalPipeProtocol.ReadFrameAsync<TokenPromptResponse>(pipe, cancellationToken);
            return response is { Accepted: true } && !string.IsNullOrWhiteSpace(response.Token)
                ? response.Token.Trim().ToCharArray()
                : null;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}
