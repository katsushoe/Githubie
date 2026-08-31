#nullable enable

namespace Githubie.ApprovalPrompt;

partial class TokenForm
{
    private System.ComponentModel.IContainer? components;
    private TableLayoutPanel rootLayout = null!;
    private Label instructionLabel = null!;
    private Label projectNameCaptionLabel = null!;
    private Label projectNameValueLabel = null!;
    private Label repositoryUrlCaptionLabel = null!;
    private Label repositoryUrlValueLabel = null!;
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
        projectNameCaptionLabel = new Label();
        projectNameValueLabel = new Label();
        repositoryUrlCaptionLabel = new Label();
        repositoryUrlValueLabel = new Label();
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
        rootLayout.Controls.Add(projectNameCaptionLabel, 0, 1);
        rootLayout.Controls.Add(projectNameValueLabel, 0, 2);
        rootLayout.Controls.Add(repositoryUrlCaptionLabel, 0, 3);
        rootLayout.Controls.Add(repositoryUrlValueLabel, 0, 4);
        rootLayout.Controls.Add(tokenTextBox, 0, 5);
        rootLayout.Controls.Add(buttonPanel, 0, 6);
        rootLayout.Dock = DockStyle.Fill;
        rootLayout.Padding = new Padding(16);
        rootLayout.RowCount = 7;
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        instructionLabel.AutoSize = true;
        instructionLabel.Margin = new Padding(0, 0, 0, 8);
        instructionLabel.Text = "Personal Access Tokenを入力してください。";
        projectNameCaptionLabel.AutoSize = true;
        projectNameCaptionLabel.Margin = new Padding(0, 4, 0, 2);
        projectNameCaptionLabel.Text = "登録元プロジェクト名";
        projectNameValueLabel.AutoSize = true;
        projectNameValueLabel.Margin = new Padding(0, 0, 0, 6);
        repositoryUrlCaptionLabel.AutoSize = true;
        repositoryUrlCaptionLabel.Margin = new Padding(0, 4, 0, 2);
        repositoryUrlCaptionLabel.Text = "登録対象リポジトリURL";
        repositoryUrlValueLabel.AutoEllipsis = true;
        repositoryUrlValueLabel.Dock = DockStyle.Fill;
        repositoryUrlValueLabel.Margin = new Padding(0, 0, 0, 10);
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
        ClientSize = new Size(600, 250);
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
