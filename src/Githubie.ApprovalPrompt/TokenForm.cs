using System.Runtime.InteropServices;
using Githubie.Application.Interactive;

namespace Githubie.ApprovalPrompt;

internal sealed partial class TokenForm : Form
{
    public TokenForm(TokenPromptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        InitializeComponent();
        projectNameValueLabel.Text = request.ProjectName;
        repositoryUrlValueLabel.Text = string.IsNullOrWhiteSpace(request.RepositoryUrl)
            ? "登録情報から取得できません"
            : request.RepositoryUrl;
        ActiveControl = tokenTextBox;
    }

    public string Token => tokenTextBox.Text;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        BeginInvoke(EnsureForeground);
    }

    private void TokenTextChanged(object? sender, EventArgs e) =>
        okButton.Enabled = !string.IsNullOrWhiteSpace(tokenTextBox.Text);

    private void EnsureForeground()
    {
        WindowState = FormWindowState.Normal;
        var desktop = Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea;
        Location = new Point(
            desktop.Left + Math.Max(0, (desktop.Width - Width) / 2),
            desktop.Top + Math.Max(0, (desktop.Height - Height) / 2));
        TopMost = false;
        TopMost = true;
        NativeMethods.SetWindowPos(Handle, NativeMethods.TopMost, 0, 0, 0, 0,
            NativeMethods.NoMove | NativeMethods.NoSize | NativeMethods.ShowWindow);

        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundThread = foreground == IntPtr.Zero
            ? 0
            : NativeMethods.GetWindowThreadProcessId(foreground, IntPtr.Zero);
        var currentThread = NativeMethods.GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != currentThread
            && NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            NativeMethods.BringWindowToTop(Handle);
            NativeMethods.SetForegroundWindow(Handle);
            Activate();
            tokenTextBox.Focus();
        }
        finally
        {
            if (attached) NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    private static class NativeMethods
    {
        internal static readonly IntPtr TopMost = new(-1);
        internal const uint NoSize = 0x0001;
        internal const uint NoMove = 0x0002;
        internal const uint ShowWindow = 0x0040;

        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);
        [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AttachThreadInput(uint attach, uint attachTo, bool attachState);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool BringWindowToTop(IntPtr window);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr window);
    }
}
