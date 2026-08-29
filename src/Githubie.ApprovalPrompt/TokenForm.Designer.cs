#nullable enable

namespace Githubie.ApprovalPrompt;

partial class TokenForm
{
    private System.ComponentModel.IContainer? components;
    private TableLayoutPanel rootLayout = null!;
    private Label instructionLabel = null!;
    private TextBox tokenTextBox = null!;
    private FlowLayoutPanel buttonPanel = null!;
    private Button okButton = null!;
    private Button cancelButton = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        rootLayout = new TableLayoutPanel();
        instructionLabel = new Label();
        tokenTextBox = new TextBox();
        buttonPanel = new FlowLayoutPanel();
        okButton = new Button();
        cancelButton = new Button();
        rootLayout.SuspendLayout();
        buttonPanel.SuspendLayout();
        SuspendLayout();
        rootLayout.ColumnCount = 1;
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.Controls.Add(instructionLabel, 0, 0);
        rootLayout.Controls.Add(tokenTextBox, 0, 1);
        rootLayout.Controls.Add(buttonPanel, 0, 2);
        rootLayout.Dock = DockStyle.Fill;
        rootLayout.Padding = new Padding(16);
        rootLayout.RowCount = 3;
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        instructionLabel.AutoSize = true;
        instructionLabel.Margin = new Padding(0, 0, 0, 8);
        instructionLabel.Text = "Personal Access Tokenを入力してください。";
        tokenTextBox.Dock = DockStyle.Top;
        tokenTextBox.Margin = new Padding(0);
        tokenTextBox.UseSystemPasswordChar = true;
        tokenTextBox.TextChanged += TokenTextChanged;
        buttonPanel.AutoSize = true;
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(okButton);
        buttonPanel.Dock = DockStyle.Bottom;
        buttonPanel.FlowDirection = FlowDirection.RightToLeft;
        buttonPanel.Margin = new Padding(0, 20, 0, 0);
        okButton.DialogResult = DialogResult.OK;
        okButton.Enabled = false;
        okButton.Size = new Size(96, 32);
        okButton.Text = "OK";
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Size = new Size(96, 32);
        cancelButton.Text = "キャンセル";
        AcceptButton = okButton;
        CancelButton = cancelButton;
        ClientSize = new Size(520, 150);
        Controls.Add(rootLayout);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.Manual;
        Text = "Githubie - Token登録";
        TopMost = true;
        rootLayout.ResumeLayout(false);
        rootLayout.PerformLayout();
        buttonPanel.ResumeLayout(false);
        ResumeLayout(false);
    }
}
