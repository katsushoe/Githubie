using System.IO.Pipes;
using Githubie.Application.Interactive;

namespace Githubie.ApprovalPrompt;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 1) return;
        ApplicationConfiguration.Initialize();
        RunAsync(args[0]).GetAwaiter().GetResult();
    }

    private static async Task RunAsync(string pipeName)
    {
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await pipe.ConnectAsync(timeout.Token); } catch (Exception exception) when (exception is IOException or OperationCanceledException) { return; }
        var request = await ApprovalPipeProtocol.ReadFrameAsync<ApprovalPromptRequest>(pipe, CancellationToken.None);
        if (request is null) return;
        using var form = new ApprovalForm(request);
        form.Shown += (_, _) => { form.Activate(); form.BringToFront(); };
        var approved = form.ShowDialog() == DialogResult.Yes;
        try { await ApprovalPipeProtocol.WriteFrameAsync(pipe, new ApprovalPromptResponse(approved), CancellationToken.None); }
        catch (IOException) { }
    }
}
