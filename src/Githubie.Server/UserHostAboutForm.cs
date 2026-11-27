using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace Githubie.Server;

internal sealed partial class UserHostAboutForm : Form
{
    public UserHostAboutForm()
    {
        InitializeComponent();
        var assembly = typeof(UserHostAboutForm).Assembly;
        var resources = new ComponentResourceManager(typeof(UserHostAboutForm));
        productLabel.Text = assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product;
        versionLabel.Text = string.Format(CultureInfo.CurrentCulture,
            resources.GetString("VersionFormat")!, assembly.GetName().Version);
        publisherLabel.Text = string.Format(CultureInfo.CurrentCulture,
            resources.GetString("PublisherFormat")!, assembly.GetCustomAttribute<AssemblyCompanyAttribute>()!.Company);
    }
}
