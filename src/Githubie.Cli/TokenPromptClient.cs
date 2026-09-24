using System.Diagnostics;
using System.IO.Pipes;
using Githubie.Application.Interactive;

namespace Githubie.Cli;

/// <summary>Token画面を起動し、入力完了またはキャンセルまで時間制限なしで待機します。</summary>
internal sealed class TokenPromptClient(string executablePath,
    Func<ProcessStartInfo, Process?>? startProcess = null)
{
    public async Task<char[]?> RequestAsync(TokenPromptRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!File.Exists(executablePath)) throw new FileNotFoundException("Token dialog executable was not found.");

        var pipeName = $"githubie-token-{Guid.NewGuid():N}";
        await using var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
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

        using var process = (startProcess ?? Process.Start)(startInfo);
        if (process is null) throw new IOException("Token dialog could not be started.");
        try
        {
            // 接続前に画面Processが終了した場合だけ待機を打ち切る。接続後の終了はPipe切断で検出される。
            if (!await TokenPromptConnection.WaitForConnectionOrExitAsync(
                    pipe, process.WaitForExitAsync(cancellationToken), cancellationToken))
            {
                throw new IOException("Token dialog exited before connecting.");
            }

            await ApprovalPipeProtocol.WriteFrameAsync(pipe, request, cancellationToken);
            var response = await ApprovalPipeProtocol.ReadFrameAsync<TokenPromptResponse>(pipe, cancellationToken);
            if (response is null) throw new IOException("Token dialog returned an invalid response.");
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
