using System.Diagnostics;
using System.Globalization;
using System.Resources;
using Githubie.Infrastructure.Configuration;
using Microsoft.Win32;

namespace Githubie.Server;

/// <summary>HTTPホストと同じプロセスのSTAスレッドでトレイを管理します。</summary>
internal static class UserHostApplication
{
    private static readonly ResourceManager Strings = new("Githubie.Server.UserHostStrings", typeof(UserHostApplication).Assembly);
    private static string Text(string name) => Strings.GetString(name, CultureInfo.CurrentUICulture) ?? name;

    public static Task<int> RunAsync(string[] args)
    {
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => Run(args, completion)) { IsBackground = false, Name = "Githubie user host tray" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static void Run(string[] args, TaskCompletionSource<int> completion)
    {
        try
        {
            System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            using var cancellation = new CancellationTokenSource();
            var executable = Path.Combine(AppContext.BaseDirectory, "Githubie.Server.exe");
            var startup = new UserAutostart(executable);
            var restart = 0;
            using var context = new UserHostTrayContext(startup, cancellation.Cancel,
                () => { Interlocked.Exchange(ref restart, 1); cancellation.Cancel(); });
            SessionEndingEventHandler ending = (_, _) => { Interlocked.Exchange(ref restart, 0); cancellation.Cancel(); };
            SystemEvents.SessionEnding += ending;
            var result = 1;
            EventHandler? start = null;
            start = async (_, _) =>
            {
                System.Windows.Forms.Application.Idle -= start;
                try
                {
                    result = await HostRuntime.RunAsync(args, cancellation.Token);
                    if (result != 0) ShowError("HostFailed", new InvalidOperationException("Host startup failed."));
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { result = 0; }
                catch (Exception exception) { ShowError("HostFailed", exception); }
                finally
                {
                    context.HideTray();
                    SystemEvents.SessionEnding -= ending;
                    context.ExitThread();
                }
            };
            System.Windows.Forms.Application.Idle += start;
            System.Windows.Forms.Application.Run(context);
            if (Volatile.Read(ref restart) != 0 && result == 0)
            {
                var launch = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
                foreach (var argument in args) launch.ArgumentList.Add(argument);
                using var child = Process.Start(launch) ?? throw new IOException("The user host could not restart.");
            }
            completion.TrySetResult(result);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Githubie tray failed: {exception.GetType().Name}");
            completion.TrySetResult(1);
        }
    }

    internal static void ShowError(string key, Exception exception)
    {
        Console.Error.WriteLine($"Githubie user host: {key} ({exception.GetType().Name})");
        MessageBox.Show(Text(key), "Githubie", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
