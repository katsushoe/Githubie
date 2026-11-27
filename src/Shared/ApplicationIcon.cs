using System.Drawing;
using System.Windows.Forms;

namespace Githubie.Desktop;

internal static class ApplicationIcon
{
    // Shared for the lifetime of the application, like SystemIcons.
    internal static Icon Default { get; } = Load(new Size(32, 32));
    internal static Icon Tray { get; } = Load(SystemInformation.SmallIconSize);

    private static Icon Load(Size size)
    {
        using var stream = typeof(ApplicationIcon).Assembly.GetManifestResourceStream("Githubie.Icon.ico")
            ?? throw new InvalidOperationException("The Githubie icon resource is missing.");
        return new Icon(stream, size);
    }
}
