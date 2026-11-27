#nullable enable

namespace Githubie.Server;

partial class UserHostTrayContext
{
    private System.ComponentModel.IContainer? components;
    private ContextMenuStrip trayMenu = null!;
    private ToolStripMenuItem autostartMenu = null!;
    private ToolStripMenuItem settingsMenu = null!;
    private ToolStripMenuItem aboutMenu = null!;
    private ToolStripMenuItem restartMenu = null!;
    private ToolStripMenuItem stopMenu = null!;
    private NotifyIcon trayIcon = null!;
    private ToolStripMenuItem serverMenu = null!;
    private ToolStripMenuItem serverStatusMenu = null!;
    private ToolStripMenuItem serverStartMenu = null!;
    private ToolStripMenuItem serverStopMenu = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _serverOn.Dispose();
            _serverOff.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        var resources = new System.ComponentModel.ComponentResourceManager(typeof(UserHostTrayContext));
        trayMenu = new ContextMenuStrip(components);
        autostartMenu = new ToolStripMenuItem();
        settingsMenu = new ToolStripMenuItem();
        aboutMenu = new ToolStripMenuItem();
        restartMenu = new ToolStripMenuItem();
        stopMenu = new ToolStripMenuItem();
        trayIcon = new NotifyIcon(components);
        serverMenu = new ToolStripMenuItem();
        serverStatusMenu = new ToolStripMenuItem();
        serverStartMenu = new ToolStripMenuItem();
        serverStopMenu = new ToolStripMenuItem();
        trayMenu.SuspendLayout();
        resources.ApplyResources(autostartMenu, "autostartMenu");
        autostartMenu.Name = "autostartMenu";
        autostartMenu.CheckOnClick = false;
        autostartMenu.Click += AutostartClicked;
        resources.ApplyResources(settingsMenu, "settingsMenu");
        settingsMenu.Name = "settingsMenu";
        settingsMenu.Click += SettingsClicked;
        resources.ApplyResources(aboutMenu, "aboutMenu");
        aboutMenu.Name = "aboutMenu";
        aboutMenu.Click += AboutClicked;
        resources.ApplyResources(restartMenu, "restartMenu");
        restartMenu.Name = "restartMenu";
        restartMenu.Click += RestartClicked;
        resources.ApplyResources(stopMenu, "stopMenu");
        stopMenu.Name = "stopMenu";
        stopMenu.Click += StopClicked;
        resources.ApplyResources(serverMenu, "serverMenu");
        resources.ApplyResources(serverStatusMenu, "serverStatusMenu");
        resources.ApplyResources(serverStartMenu, "serverStartMenu");
        resources.ApplyResources(serverStopMenu, "serverStopMenu");
        serverStartMenu.Click += ServerStartClicked;
        serverStopMenu.Click += ServerStopClicked;
        serverMenu.DropDownItems.AddRange([serverStatusMenu, serverStartMenu, serverStopMenu]);
        trayMenu.Items.AddRange([serverMenu, new ToolStripSeparator(), settingsMenu, autostartMenu, aboutMenu, restartMenu, stopMenu]);
        trayMenu.Name = "trayMenu";
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.DoubleClick += SettingsClicked;
        trayIcon.Icon = Githubie.Desktop.ApplicationIcon.Tray;
        resources.ApplyResources(trayIcon, "trayIcon");
        trayIcon.Visible = false;
        trayMenu.ResumeLayout(false);
    }
}
