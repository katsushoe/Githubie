#nullable enable

namespace Githubie.Server;

partial class UserHostAboutForm
{
    private Label productLabel = null!;
    private Label descriptionLabel = null!;
    private Label versionLabel = null!;
    private Label publisherLabel = null!;
    private Button closeButton = null!;

    private void InitializeComponent()
    {
        var resources = new System.ComponentModel.ComponentResourceManager(typeof(UserHostAboutForm));
        productLabel = new Label();
        descriptionLabel = new Label();
        versionLabel = new Label();
        publisherLabel = new Label();
        closeButton = new Button();
        SuspendLayout();

        productLabel.AutoSize = true;
        productLabel.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
        productLabel.Location = new Point(20, 18);
        productLabel.Name = "productLabel";

        descriptionLabel.AutoSize = true;
        descriptionLabel.Location = new Point(20, 58);
        descriptionLabel.MaximumSize = new Size(376, 0);
        descriptionLabel.Name = "descriptionLabel";
        resources.ApplyResources(descriptionLabel, "descriptionLabel");

        versionLabel.AutoSize = true;
        versionLabel.Location = new Point(20, 105);
        versionLabel.Name = "versionLabel";

        publisherLabel.AutoSize = true;
        publisherLabel.Location = new Point(20, 131);
        publisherLabel.Name = "publisherLabel";

        closeButton.DialogResult = DialogResult.OK;
        closeButton.Location = new Point(310, 169);
        closeButton.Name = "closeButton";
        closeButton.Size = new Size(86, 30);
        closeButton.TabIndex = 0;
        resources.ApplyResources(closeButton, "closeButton");

        AcceptButton = closeButton;
        CancelButton = closeButton;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(416, 218);
        Controls.Add(productLabel);
        Controls.Add(descriptionLabel);
        Controls.Add(versionLabel);
        Controls.Add(publisherLabel);
        Controls.Add(closeButton);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        StartPosition = FormStartPosition.CenterScreen;
        resources.ApplyResources(this, "$this");
        ResumeLayout(false);
        PerformLayout();
    }
}
