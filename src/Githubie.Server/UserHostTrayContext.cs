using Githubie.Infrastructure.Configuration;

namespace Githubie.Server;

internal sealed partial class UserHostTrayContext : ApplicationContext
{
    private readonly UserAutostart _startup;
    private readonly Action _stop;
    private readonly Action _restart;
    private StartupUpdater? _updater;
    private UserHostSettingsForm? _settings;
    private UserHostAboutForm? _about;
    private CancellationToken _lifetime;
    private string? _previousUpdate;
    private string _updateStatus = "UpdateIdle";
    private bool _hostReady;
    private bool _checking;

    public UserHostTrayContext(UserAutostart startup, Action stop, Action restart, bool showTray = true)
    {
        _startup = startup;
        _stop = stop;
        _restart = restart;
        InitializeComponent();
        autostartMenu.Checked = _startup.IsEnabled();
        trayIcon.Visible = showTray;
    }

    public void HideTray()
    {
        trayIcon.Visible = false;
        _settings?.Close();
        _about?.Close();
    }

    public void ConfigureUpdates(StartupUpdater updater, string? previousUpdate, CancellationToken lifetime)
    {
        _updater = updater;
        _previousUpdate = previousUpdate;
        _lifetime = lifetime;
    }

    public async void HostReady()
    {
        if (_lifetime.IsCancellationRequested) return;
        _hostReady = true;
        if (_previousUpdate is "UpdateInstalled" or "UpdateCancelled" or "UpdateFailed" or "UpdateRebootRequired")
        {
            SetStatus(_previousUpdate);
            return;
        }
        if (_previousUpdate is not null) return;
        try
        {
            if (_startup.ReadSettings().StartupUpdateEnabled) await CheckAsync(install: true);
            else SetStatus("UpdateIdle");
        }
        catch (Exception exception) when (IsSettingsError(exception))
        {
            SetStatus("UpdateFailed");
        }
    }

    private async Task CheckAsync(bool install)
    {
        if (!_hostReady || _checking || _updater is null || _lifetime.IsCancellationRequested) return;
        _checking = true;
        SetStatus("UpdateChecking");
        try
        {
            var status = await _updater.PrepareAsync(install ? () => _startup.ReadSettings().StartupUpdateEnabled : null, _lifetime);
            SetStatus(status);
            if (status == "UpdateApplying") _stop();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally
        {
            _checking = false;
            if (!_lifetime.IsCancellationRequested) SetStatus(_updateStatus);
        }
    }

    private void SetStatus(string status)
    {
        _updateStatus = status;
        _settings?.ShowUpdateStatus(status, _hostReady && !_checking);
    }

    private void SettingsClicked(object? sender, EventArgs args)
    {
        if (_settings is not null)
        {
            _settings.Activate();
            return;
        }
        try
        {
            var current = _startup.ReadSettings();
            using var dialog = new UserHostSettingsForm(_startup.ConfigurationPath, current,
                (autostart, update) => autostartMenu.Checked = _startup.SaveSettings(autostart, update),
                () => CheckAsync(install: false));
            _settings = dialog;
            SetStatus(_updateStatus);
            dialog.ShowDialog();
        }
        catch (Exception exception) when (IsSettingsError(exception))
        {
            UserHostApplication.ShowError("SettingsFailed", exception);
        }
        finally { _settings = null; }
    }

    internal static bool IsSettingsError(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.SecurityException or System.Text.Json.JsonException;

    private void AboutClicked(object? sender, EventArgs args)
    {
        if (_about is not null)
        {
            _about.Activate();
            return;
        }
        using var dialog = new UserHostAboutForm();
        _about = dialog;
        try { dialog.ShowDialog(); }
        finally { _about = null; }
    }

    private void AutostartClicked(object? sender, EventArgs args)
    {
        try { autostartMenu.Checked = _startup.SetEnabled(!_startup.IsEnabled()); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { UserHostApplication.ShowError("StartupFailed", exception); }
    }

    private void RestartClicked(object? sender, EventArgs args) => _restart();
    private void StopClicked(object? sender, EventArgs args) => _stop();
}
