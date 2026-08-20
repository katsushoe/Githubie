#nullable enable

namespace Githubie.ApprovalPrompt;

partial class ApprovalForm
{
    private System.ComponentModel.IContainer? components;
    private Label operationCaptionLabel = null!;
    private Label operationValueLabel = null!;
    private Label summaryCaptionLabel = null!;
    private Label summaryValueLabel = null!;
    private Label detailsCaptionLabel = null!;
    private Label detailsValueLabel = null!;
    private Label countdownLabel = null!;
    private Button approveButton = null!;
    private Button denyButton = null!;
    private System.Windows.Forms.Timer countdownTimer = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        operationCaptionLabel = new Label();
        operationValueLabel = new Label();
        summaryCaptionLabel = new Label();
        summaryValueLabel = new Label();
        detailsCaptionLabel = new Label();
        detailsValueLabel = new Label();
        countdownLabel = new Label();
        approveButton = new Button();
        denyButton = new Button();
        countdownTimer = new System.Windows.Forms.Timer(components);
        SuspendLayout();
        operationCaptionLabel.AutoSize = true;
        operationCaptionLabel.Font = new Font(Font, FontStyle.Bold);
        operationCaptionLabel.Location = new Point(16, 16);
        operationCaptionLabel.Text = "Operation";
        operationValueLabel.AutoSize = true;
        operationValueLabel.Location = new Point(16, 34);
        operationValueLabel.MaximumSize = new Size(608, 0);
        summaryCaptionLabel.AutoSize = true;
        summaryCaptionLabel.Font = new Font(Font, FontStyle.Bold);
        summaryCaptionLabel.Location = new Point(16, 56);
        summaryCaptionLabel.Text = "Summary";
        summaryValueLabel.AutoSize = true;
        summaryValueLabel.Location = new Point(16, 74);
        summaryValueLabel.MaximumSize = new Size(608, 0);
        detailsCaptionLabel.AutoSize = true;
        detailsCaptionLabel.Font = new Font(Font, FontStyle.Bold);
        detailsCaptionLabel.Location = new Point(16, 116);
        detailsCaptionLabel.Text = "Details";
        detailsValueLabel.AutoEllipsis = true;
        detailsValueLabel.Location = new Point(16, 134);
        detailsValueLabel.MaximumSize = new Size(608, 240);
        detailsValueLabel.Size = new Size(608, 240);
        countdownLabel.AutoSize = true;
        countdownLabel.Location = new Point(16, 400);
        countdownLabel.Text = "Auto-deny in 120s if no response.";
        approveButton.DialogResult = DialogResult.Yes;
        approveButton.Location = new Point(392, 428);
        approveButton.Size = new Size(110, 32);
        approveButton.Text = "&Approve";
        denyButton.DialogResult = DialogResult.No;
        denyButton.Location = new Point(512, 428);
        denyButton.Size = new Size(110, 32);
        denyButton.Text = "&Deny";
        countdownTimer.Interval = 1000;
        countdownTimer.Tick += CountdownTimerTick;
        AcceptButton = null;
        CancelButton = denyButton;
        ClientSize = new Size(640, 480);
        Controls.Add(operationCaptionLabel);
        Controls.Add(operationValueLabel);
        Controls.Add(summaryCaptionLabel);
        Controls.Add(summaryValueLabel);
        Controls.Add(detailsCaptionLabel);
        Controls.Add(detailsValueLabel);
        Controls.Add(countdownLabel);
        Controls.Add(approveButton);
        Controls.Add(denyButton);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ResumeLayout(false);
        PerformLayout();
    }
}
