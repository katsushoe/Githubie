using System.IO.Pipes;
using Githubie.Application.Interactive;

namespace Githubie.ApprovalPrompt;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args is not ["--token", _] && args is not [_]) return;

        // Pipe待機後もSTA UIスレッドへ戻り、ダイアログ終了後の応答送信も処理します。
        using var context = new ApplicationContext();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        EventHandler? start = null;
        start = async (_, _) =>
        {
            System.Windows.Forms.Application.Idle -= start;
            try
            {
                if (args is ["--token", var tokenPipe]) await RunTokenAsync(tokenPipe);
                else await RunApprovalAsync(args[0]);
            }
            finally
            {
                context.ExitThread();
            }
        };
        System.Windows.Forms.Application.Idle += start;
        System.Windows.Forms.Application.Run(context);
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
        form.ClearToken();
        try { await ApprovalPipeProtocol.WriteFrameAsync(pipe, response, CancellationToken.None); }
        catch (IOException) { }
    }
}
