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

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
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
        trayMenu.Items.AddRange([settingsMenu, autostartMenu, aboutMenu, restartMenu, stopMenu]);
        trayMenu.Name = "trayMenu";
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.DoubleClick += SettingsClicked;
        trayIcon.Icon = System.Drawing.SystemIcons.Application;
        resources.ApplyResources(trayIcon, "trayIcon");
        trayIcon.Visible = false;
        trayMenu.ResumeLayout(false);
    }
}
