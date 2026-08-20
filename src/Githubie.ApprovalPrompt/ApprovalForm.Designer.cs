#nullable enable

namespace Githubie.ApprovalPrompt;

partial class ApprovalForm
{
    private System.ComponentModel.IContainer? components;
    private TableLayoutPanel rootLayout = null!;
    private Label warningLabel = null!;
    private Label summaryLabel = null!;
    private TextBox detailsTextBox = null!;
    private Label countdownLabel = null!;
    private FlowLayoutPanel buttonsPanel = null!;
    private Button approveButton = null!;
    private Button denyButton = null!;
    private System.Windows.Forms.Timer countdownTimer = null!;

    protected override void Dispose(bool disposing) { if (disposing) components?.Dispose(); base.Dispose(disposing); }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        rootLayout = new TableLayoutPanel(); warningLabel = new Label(); summaryLabel = new Label();
        detailsTextBox = new TextBox(); countdownLabel = new Label(); buttonsPanel = new FlowLayoutPanel();
        approveButton = new Button(); denyButton = new Button(); countdownTimer = new System.Windows.Forms.Timer(components);
        SuspendLayout();
        rootLayout.ColumnCount = 1; rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); rootLayout.Dock = DockStyle.Fill;
        rootLayout.Padding = new Padding(16); rootLayout.RowCount = 5;
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        warningLabel.AutoSize = true; warningLabel.Font = new Font(Font, FontStyle.Bold); warningLabel.ForeColor = Color.DarkRed;
        warningLabel.Text = "Danger: this rewrites published Git history.";
        summaryLabel.AutoSize = true; summaryLabel.Margin = new Padding(0, 12, 0, 12);
        detailsTextBox.Dock = DockStyle.Fill; detailsTextBox.Multiline = true; detailsTextBox.ReadOnly = true; detailsTextBox.ScrollBars = ScrollBars.Both;
        countdownLabel.AutoSize = true; countdownLabel.Margin = new Padding(0, 12, 0, 8); countdownLabel.Text = "Auto-deny in 120s.";
        buttonsPanel.AutoSize = true; buttonsPanel.Dock = DockStyle.Fill; buttonsPanel.FlowDirection = FlowDirection.RightToLeft;
        approveButton.DialogResult = DialogResult.Yes; approveButton.Text = "Approve rewrite"; approveButton.AutoSize = true;
        denyButton.DialogResult = DialogResult.No; denyButton.Text = "Deny"; denyButton.AutoSize = true;
        buttonsPanel.Controls.Add(denyButton); buttonsPanel.Controls.Add(approveButton);
        rootLayout.Controls.Add(warningLabel, 0, 0); rootLayout.Controls.Add(summaryLabel, 0, 1); rootLayout.Controls.Add(detailsTextBox, 0, 2);
        rootLayout.Controls.Add(countdownLabel, 0, 3); rootLayout.Controls.Add(buttonsPanel, 0, 4);
        countdownTimer.Interval = 1000; countdownTimer.Tick += CountdownTimerTick;
        AcceptButton = null; CancelButton = denyButton; ClientSize = new Size(680, 420); Controls.Add(rootLayout);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen; TopMost = true;
        ResumeLayout(false);
    }
}
