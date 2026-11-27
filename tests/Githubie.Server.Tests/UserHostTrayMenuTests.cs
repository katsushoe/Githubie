using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;
using Githubie.Infrastructure.Configuration;
using Xunit;

namespace Githubie.Server.Tests;

public sealed class UserHostTrayMenuTests
{
    [Theory]
    [InlineData("ja-JP", "状態", "起動", "終了")]
    [InlineData("en-US", "Status", "Start", "Stop")]
    public async Task ServerMenu_ReflectsReadinessAndDisablesUnavailableCommands(
        string culture, string status, string start, string stop)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                var startup = new UserAutostart(Path.Combine(AppContext.BaseDirectory, "Githubie.Server.exe"));
                using var context = new UserHostTrayContext(startup, () => { }, () => { },
                    () => Task.CompletedTask, () => Task.CompletedTask, showTray: false);
                var field = typeof(UserHostTrayContext).GetField("serverMenu", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var menu = Assert.IsType<ToolStripMenuItem>(field.GetValue(context));
                Assert.Equal("Server", menu.Text);
                Assert.Equal(new[] { status, start, stop }, menu.DropDownItems.Cast<ToolStripMenuItem>().Select(item => item.Text));
                var indicator = (ToolStripMenuItem)menu.DropDownItems[0];
                var startItem = menu.DropDownItems[1];
                var stopItem = menu.DropDownItems[2];

                context.SetServerState(UserHostServerState.Stopped);
                Assert.True(startItem.Enabled);
                Assert.False(stopItem.Enabled);
                Assert.Equal("OFF", indicator.AccessibleDescription);
                Assert.Equal(Color.FromArgb(64, 64, 64).ToArgb(), ((Bitmap)indicator.Image!).GetPixel(8, 8).ToArgb());

                context.SetServerState(UserHostServerState.Starting);
                Assert.False(startItem.Enabled);
                Assert.True(stopItem.Enabled);
                Assert.Equal("OFF", indicator.AccessibleDescription);

                context.SetServerState(UserHostServerState.Running);
                Assert.False(startItem.Enabled);
                Assert.True(stopItem.Enabled);
                Assert.True(indicator.Enabled);
                Assert.Equal("ON", indicator.AccessibleDescription);
                Assert.Equal(Color.LimeGreen.ToArgb(), ((Bitmap)indicator.Image!).GetPixel(8, 8).ToArgb());

                context.SetServerState(UserHostServerState.Stopping);
                Assert.False(startItem.Enabled);
                Assert.False(stopItem.Enabled);
                Assert.Equal("OFF", indicator.AccessibleDescription);
                completion.SetResult();
            }
            catch (Exception exception) { completion.SetException(exception); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
    }
}
