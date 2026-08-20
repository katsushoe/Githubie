using Githubie.Application.Interactive;

namespace Githubie.ApprovalPrompt;

internal sealed partial class ApprovalForm : Form
{
    private int _remainingSeconds = 120;

    public ApprovalForm(ApprovalPromptRequest request)
    {
        InitializeComponent();
        Text = request.Title;
        summaryLabel.Text = request.Summary;
        detailsTextBox.Lines = request.Details.ToArray();
        countdownTimer.Start();
    }

    private void CountdownTimerTick(object? sender, EventArgs e)
    {
        _remainingSeconds--;
        countdownLabel.Text = $"Auto-deny in {_remainingSeconds}s.";
        if (_remainingSeconds > 0) return;
        countdownTimer.Stop();
        DialogResult = DialogResult.No;
        Close();
    }
}
