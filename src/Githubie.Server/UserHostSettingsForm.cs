using Githubie.Application.Configuration;

namespace Githubie.Server;

internal sealed partial class UserHostSettingsForm : Form
{
    private readonly Action<bool, bool> _save;
    private readonly Func<Task> _check;

    public UserHostSettingsForm(string path, UserHostOptions settings, Action<bool, bool> save, Func<Task> check)
    {
        _save = save;
        _check = check;
        InitializeComponent();
        pathText.Text = path;
        autostartCheck.Checked = settings.AutostartEnabled;
        updateCheck.Checked = settings.StartupUpdateEnabled;
    }

    public void ShowUpdateStatus(string status, bool canCheck)
    {
        statusLabel.Text = UserHostApplication.Text(status);
        checkButton.Enabled = canCheck;
    }

    private void SaveClicked(object? sender, EventArgs args)
    {
        try
        {
            _save(autostartCheck.Checked, updateCheck.Checked);
            Close();
        }
        catch (Exception exception) when (UserHostTrayContext.IsSettingsError(exception))
        {
            UserHostApplication.ShowError("SettingsFailed", exception);
        }
    }

    private async void CheckClicked(object? sender, EventArgs args) => await _check();
}
