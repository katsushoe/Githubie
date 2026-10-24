using Githubie.Infrastructure.Configuration;

namespace Githubie.Server;

internal sealed partial class UserHostTrayContext : ApplicationContext
{
    private readonly UserAutostart _startup;
    private readonly Action _stop;
    private readonly Action _restart;

    public UserHostTrayContext(UserAutostart startup, Action stop, Action restart, bool showTray = true)
    {
        _startup = startup;
        _stop = stop;
        _restart = restart;
        InitializeComponent();
        autostartMenu.Checked = _startup.IsEnabled();
        trayIcon.Visible = showTray;
    }

    public void HideTray() => trayIcon.Visible = false;

    private void AutostartClicked(object? sender, EventArgs args)
    {
        try { autostartMenu.Checked = _startup.SetEnabled(!_startup.IsEnabled()); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { UserHostApplication.ShowError("StartupFailed", exception); }
    }

    private void RestartClicked(object? sender, EventArgs args) => _restart();
    private void StopClicked(object? sender, EventArgs args) => _stop();
}
