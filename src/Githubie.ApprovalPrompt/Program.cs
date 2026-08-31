using System.IO.Pipes;
using Githubie.Application.Interactive;

namespace Githubie.ApprovalPrompt;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args is ["--token", var tokenPipe])
        {
            RunTokenAsync(tokenPipe).GetAwaiter().GetResult();
            return;
        }

        if (args is [var approvalPipe]) RunApprovalAsync(approvalPipe).GetAwaiter().GetResult();
    }

    private static async Task RunApprovalAsync(string pipeName)
    {
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await pipe.ConnectAsync(timeout.Token); } catch (Exception exception) when (exception is IOException or OperationCanceledException) { return; }
        var request = await ApprovalPipeProtocol.ReadFrameAsync<ApprovalPromptRequest>(pipe, CancellationToken.None);
        if (request is null) return;
        using var form = new ApprovalForm(request);
        var approved = form.ShowDialog() == DialogResult.Yes;
        try { await ApprovalPipeProtocol.WriteFrameAsync(pipe, new ApprovalPromptResponse(approved), CancellationToken.None); }
        catch (IOException) { }
    }

    private static async Task RunTokenAsync(string pipeName)
    {
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await pipe.ConnectAsync(timeout.Token); }
        catch (Exception exception) when (exception is IOException or OperationCanceledException) { return; }

        var request = await ApprovalPipeProtocol.ReadFrameAsync<TokenPromptRequest>(pipe, CancellationToken.None);
        if (request is null) return;
        using var form = new TokenForm(request);
        var accepted = form.ShowDialog() == DialogResult.OK;
        var response = new TokenPromptResponse(accepted, accepted ? form.Token : string.Empty);
        try { await ApprovalPipeProtocol.WriteFrameAsync(pipe, response, CancellationToken.None); }
        catch (IOException) { }
    }
}
