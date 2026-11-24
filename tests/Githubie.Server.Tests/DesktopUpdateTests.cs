using System.Text.Json;
using System.Globalization;
using System.Windows.Forms;
using Githubie.Application.Configuration;
using Githubie.Infrastructure.Configuration;
using Xunit;

namespace Githubie.Server.Tests;

public sealed class DesktopUpdateTests
{
    [Theory]
    [InlineData("en-US", "About Githubie…", "About Githubie", "Version", "Publisher", "Close")]
    [InlineData("ja-JP", "バージョン情報…", "Githubie バージョン情報", "バージョン", "発行元", "閉じる")]
    public async Task AboutForm_WhenOpened_DisplaysLocalizedProductMetadata(string culture, string menu,
        string title, string versionLabel, string publisherLabel, string closeLabel)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ui = new Thread(() =>
        {
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                var menuResources = new System.ComponentModel.ComponentResourceManager(typeof(UserHostTrayContext));
                Assert.Equal(menu, menuResources.GetString("aboutMenu.Text"));
                using var form = new UserHostAboutForm();
                Assert.Equal(title, form.Text);
                var labels = form.Controls.OfType<Label>().ToDictionary(label => label.Name, label => label.Text);
                Assert.Equal("Githubie MCP Server", labels["productLabel"]);
                Assert.Equal($"{versionLabel} {typeof(UserHostAboutForm).Assembly.GetName().Version}", labels["versionLabel"]);
                Assert.Equal($"{publisherLabel}: Akatsukisoft", labels["publisherLabel"]);
                Assert.False(string.IsNullOrWhiteSpace(labels["descriptionLabel"]));
                var close = Assert.IsType<Button>(form.AcceptButton);
                Assert.Same(close, form.CancelButton);
                Assert.Equal(closeLabel, close.Text);
                completion.SetResult();
            }
            catch (Exception exception) { completion.SetException(exception); }
        }) { IsBackground = true };
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("v2.3.0.0", false, false, "github.com", true)]
    [InlineData("2.3.0.0", false, false, "github.com", true)]
    [InlineData("v2.2.0.1", false, false, "github.com", false)]
    [InlineData("v2.1.9.0", false, false, "github.com", false)]
    [InlineData("v2.3.0.0", true, false, "github.com", false)]
    [InlineData("v2.3.0.0", false, true, "github.com", false)]
    [InlineData("v2.3.0.0", false, false, "other.example", false)]
    [InlineData("v2.3.0.0-preview", false, false, "github.com", false)]
    public void SelectsOnlyNewerOfficialStableMsi(string tag, bool draft, bool prerelease, string host, bool expected)
    {
        var name = $"Githubie-{tag.TrimStart('v')}-win-x64.msi";
        using var release = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            tag_name = tag, draft, prerelease,
            assets = new[] { new { name, browser_download_url = $"https://{host}/katsushoe/Githubie/releases/download/{tag}/{name}" } }
        }));

        Assert.Equal(expected, StartupUpdater.FindPackage(release.RootElement, new Version(2, 2, 0, 0)) is not null);
    }

    [Fact]
    public async Task SettingsFormLoadsJapaneseLabelsAndSavesBothCheckboxes()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ui = new Thread(() =>
        {
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
                (bool Autostart, bool Update)? saved = null;
                const string path = @"D:\Applications\Githubie\config\githubie.json";
                using var form = new UserHostSettingsForm(path, new UserHostOptions(),
                    (autostart, update) => saved = (autostart, update), () => Task.CompletedTask);
                form.ShowInTaskbar = false;
                form.Opacity = 0;
                form.ShowUpdateStatus("UpdateNone", true);
                form.Show();
                var rows = Assert.IsType<TableLayoutPanel>(Assert.Single(form.Controls));
                var checks = rows.Controls.OfType<CheckBox>().ToArray();
                Assert.Equal("Githubie 設定", form.Text);
                Assert.Equal(path, Assert.Single(rows.Controls.OfType<TextBox>()).Text);
                Assert.Equal(2, checks.Length);
                Assert.All(checks, check => Assert.True(check.Checked));
                checks[1].Checked = false;
                var actions = Assert.Single(rows.Controls.OfType<FlowLayoutPanel>());
                Assert.Single(actions.Controls.OfType<Button>(), button => button.Text == "保存").PerformClick();
                Assert.Equal((true, false), saved);
                completion.SetResult();
            }
            catch (Exception exception) { completion.SetException(exception); }
        }) { IsBackground = true };
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void DesktopSettingsUseCanonicalFileAndPreserveOtherOptions()
    {
        var directory = Directory.CreateTempSubdirectory("GithubieDesktop-");
        try
        {
            var bin = Directory.CreateDirectory(Path.Combine(directory.FullName, "bin"));
            Directory.CreateDirectory(Path.Combine(directory.FullName, "config"));
            var config = Path.Combine(directory.FullName, "config", "githubie.json");
            File.WriteAllText(config, """{"other":{"port":1234},"user_host":{"authentication_mode":"windows_owner"}}""");
            var settings = new UserAutostart(Path.Combine(bin.FullName, "Githubie.Server.exe"), registration: new Registration());
            Assert.Equal(config, settings.ConfigurationPath);
            Assert.True(settings.ReadSettings().StartupUpdateEnabled);

            Assert.True(settings.SaveSettings(enabled: true, startupUpdateEnabled: false));
            Assert.True(settings.IsEnabled());
            using var saved = JsonDocument.Parse(File.ReadAllText(config));
            Assert.Equal(1234, saved.RootElement.GetProperty("other").GetProperty("port").GetInt32());
            Assert.Equal(UserHostAuthenticationMode.WindowsOwner, settings.ReadSettings().AuthenticationMode);
            Assert.False(settings.ReadSettings().StartupUpdateEnabled);

            Assert.False(settings.SetEnabled(false));
            Assert.False(settings.IsEnabled());
            Assert.False(settings.ReadSettings().StartupUpdateEnabled);
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed class Registration : IUserStartupRegistration
    {
        private string? _command;
        public string? Read() => _command;
        public void Write(string? command) => _command = command;
    }
}
