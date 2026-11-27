using System.Diagnostics;
using System.Globalization;
using System.Resources;
using Githubie.Application.Configuration;
using Githubie.Infrastructure.Configuration;
using Microsoft.Win32;

namespace Githubie.Server;

/// <summary>HTTPホストと同じプロセスのSTAスレッドでトレイを管理します。</summary>
internal static class UserHostApplication
{
    private static readonly ResourceManager Strings = new("Githubie.Server.UserHostStrings", typeof(UserHostApplication).Assembly);
    internal static string Text(string name) => Strings.GetString(name, CultureInfo.CurrentUICulture) ?? name;

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
            var ui = SynchronizationContext.Current!;
            using var cancellation = new CancellationTokenSource();
            var executable = Path.Combine(AppContext.BaseDirectory, "Githubie.Server.exe");
            var startup = new UserAutostart(executable);
            var restart = 0;
            var result = 0;
            FileStream? profileLock = null;
            var serverArguments = args;
            var server = new UserHostServerController((token, ready) =>
            {
                var currentArguments = serverArguments;
                // Sign-in preferences apply to the initial launch, not a manual Server > Start.
                serverArguments = args.Where(argument => !string.Equals(argument, "--autostart", StringComparison.OrdinalIgnoreCase)).ToArray();
                return HostRuntime.RunAsync(currentArguments, token,
                    onReady: () => ui.Post(_ => ready(), null), profileLockHeld: true);
            }, cancellation.Token);
            using var context = new UserHostTrayContext(startup, () => { result = 0; cancellation.Cancel(); },
                () => { result = 0; Interlocked.Exchange(ref restart, 1); cancellation.Cancel(); },
                () => profileLock is null ? Task.CompletedTask : server.StartAsync(), server.StopAsync);
            var updater = new StartupUpdater(executable, args);
            var previousUpdate = Environment.GetEnvironmentVariable(StartupUpdater.SkipEnvironmentVariable);
            Environment.SetEnvironmentVariable(StartupUpdater.SkipEnvironmentVariable, null);
            context.ConfigureUpdates(updater, previousUpdate, cancellation.Token);
            SessionEndingEventHandler ending = (_, _) => { Interlocked.Exchange(ref restart, 0); cancellation.Cancel(); };
            SystemEvents.SessionEnding += ending;
            var exiting = false;
            void Exit()
            {
                if (exiting) return;
                exiting = true;
                cancellation.Cancel();
                context.HideTray();
                context.ExitThread();
            }
            server.StateChanged += state =>
            {
                context.SetServerState(state);
                if (state == UserHostServerState.Running) context.HostReady();
            };
            server.Completed += (code, stopped, failure) =>
            {
                result = code;
                if (failure is not null) ShowError("HostFailed", failure);
                else if (code != 0) ShowError("HostFailed", new InvalidOperationException("Host startup failed."));
                if (cancellation.IsCancellationRequested || (!stopped && code == 0)) Exit();
            };
            using var closeRegistration = cancellation.Token.Register(() => ui.Post(_ =>
            {
                if (server.State == UserHostServerState.Stopped) Exit();
            }, null));
            EventHandler? start = null;
            start = async (_, _) =>
            {
                System.Windows.Forms.Application.Idle -= start;
                try
                {
                    var layout = GithubiePathLayout.ForCurrentUser(AppContext.BaseDirectory);
                    await new UserProfileStore(layout).InitializeAsync(cancellation.Token);
                    profileLock = new FileStream(Path.Combine(layout.DataDirectory, "host.lock"),
                        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    await server.StartAsync();
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { Exit(); }
                catch (Exception exception)
                {
                    result = 1;
                    ShowError("HostFailed", exception);
                    Exit();
                }
            };
            System.Windows.Forms.Application.Idle += start;
            try { System.Windows.Forms.Application.Run(context); }
            finally
            {
                System.Windows.Forms.Application.Idle -= start;
                SystemEvents.SessionEnding -= ending;
                profileLock?.Dispose();
            }
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
