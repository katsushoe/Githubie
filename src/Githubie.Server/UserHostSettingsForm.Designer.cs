#nullable enable

namespace Githubie.Server;

partial class UserHostSettingsForm
{
    private CheckBox autostartCheck = null!;
    private CheckBox updateCheck = null!;
    private Button checkButton = null!;
    private TextBox pathText = null!;
    private Label statusLabel = null!;

    private void InitializeComponent()
    {
        var text = UserHostApplication.Text;
        var rows = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16)
        };
        var versionLabel = new Label
        {
            AutoSize = true, Text = string.Format(text("SettingsVersion"), StartupUpdater.CurrentVersion)
        };
        autostartCheck = new CheckBox { AutoSize = true, Text = text("SettingsAutostart") };
        updateCheck = new CheckBox { AutoSize = true, Text = text("SettingsAutoUpdate") };
        var pathLabel = new Label { AutoSize = true, Text = text("SettingsPath") };
        pathText = new TextBox { ReadOnly = true, Dock = DockStyle.Top, MinimumSize = new Size(420, 0) };
        statusLabel = new Label { AutoSize = true, MaximumSize = new Size(420, 0), AccessibleName = text("SettingsUpdateStatus") };
        checkButton = new Button { AutoSize = true, Text = text("SettingsCheck") };
        var saveButton = new Button { AutoSize = true, Text = text("SettingsSave") };
        var cancelButton = new Button { AutoSize = true, Text = text("SettingsCancel"), DialogResult = DialogResult.Cancel };
        var actions = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false
        };
        actions.Controls.AddRange([cancelButton, saveButton, checkButton]);
        checkButton.Click += CheckClicked;
        saveButton.Click += SaveClicked;
        rows.Controls.Add(versionLabel);
        rows.Controls.Add(autostartCheck);
        rows.Controls.Add(updateCheck);
        rows.Controls.Add(pathLabel);
        rows.Controls.Add(pathText);
        rows.Controls.Add(statusLabel);
        rows.Controls.Add(actions);
        Controls.Add(rows);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        Text = text("SettingsTitle");
    }
}
