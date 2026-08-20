using Githubie.Application.Interactive;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Githubie.ApprovalPrompt;

internal sealed partial class ApprovalForm : Form
{
    private static readonly Regex RegistrationSummaryPattern = new(
        "^Register '(?<repository>.+)' for (?<target>.+)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex RewriteSummaryPattern = new(
        "^Rewrite (?<count>[0-9]+) published ref\\(s\\) in (?<repository>.+)$",
        RegexOptions.CultureInvariant);
    private int _remainingSeconds = 120;

    public ApprovalForm(ApprovalPromptRequest request)
    {
        InitializeComponent();
        ApplyRequestText(request);
        detailsTextBox.Select(0, 0);
        ActiveControl = denyButton;
        countdownTimer.Start();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        BeginInvoke(EnsureForeground);
    }

    private void EnsureForeground()
    {
        WindowState = FormWindowState.Normal;
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
            denyButton.Focus();
        }
        finally
        {
            if (attached) NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    private void CountdownTimerTick(object? sender, EventArgs e)
    {
        _remainingSeconds--;
        countdownLabel.Text = IsJapanese
            ? $"{_remainingSeconds}秒後に自動的に拒否します。"
            : $"Auto-deny in {_remainingSeconds}s.";
        if (_remainingSeconds > 0) return;
        countdownTimer.Stop();
        DialogResult = DialogResult.No;
        Close();
    }

    private static bool IsJapanese =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ja", StringComparison.OrdinalIgnoreCase);

    private void ApplyRequestText(ApprovalPromptRequest request)
    {
        if (!IsJapanese)
        {
            Text = request.Title;
            summaryLabel.Text = request.Summary;
            detailsTextBox.Lines = request.Details.ToArray();
            return;
        }

        var isRegistration = request.Title.Equals("Githubie repository registration", StringComparison.Ordinal);
        Text = isRegistration ? "Githubie - リポジトリ登録の承認" : "Githubie - 履歴書き換えの承認";
        warningLabel.Text = isRegistration
            ? "確認: このリポジトリの登録を承認しますか。"
            : "危険: 公開済みのGit履歴を書き換えます。";
        summaryLabel.Text = TranslateSummary(request.Summary);
        detailsTextBox.Lines = request.Details.Select(TranslateDetail).ToArray();
        countdownLabel.Text = "120秒後に自動的に拒否します。";
        approveButton.Text = isRegistration ? "登録を承認" : "書き換えを承認";
        denyButton.Text = "拒否";
    }

    private static string TranslateSummary(string summary)
    {
        var registrationMatch = RegistrationSummaryPattern.Match(summary);
        if (registrationMatch.Success)
            return $"'{registrationMatch.Groups["repository"].Value}' を {registrationMatch.Groups["target"].Value} として登録します。";

        var rewriteMatch = RewriteSummaryPattern.Match(summary);
        if (rewriteMatch.Success)
            return $"{rewriteMatch.Groups["repository"].Value} の公開済み参照 {rewriteMatch.Groups["count"].Value} 件を書き換えます。";

        return summary;
    }

    private static string TranslateDetail(string detail)
    {
        if (detail.StartsWith("Local root: ", StringComparison.Ordinal))
            return $"ローカルルート: {detail[12..]}";
        if (detail.StartsWith("Remote: ", StringComparison.Ordinal))
            return $"リモート: {detail[8..]}";
        if (detail.StartsWith("Branches: ", StringComparison.Ordinal))
            return $"ブランチ: {detail[10..]}";
        return detail;
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
