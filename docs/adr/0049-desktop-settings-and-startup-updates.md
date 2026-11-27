# 0049. Desktop settings and startup updates

The interactive Windows host reuses its taskbar notification icon. The settings
window saves sign-in startup and startup updates in the installation's canonical
`config\githubie.json`. The updater is enabled by default for MSI installations
and starts only after the interactive host is ready.

Automatic installation uses the latest stable release from the public Githubie
release repository. A candidate must have a strictly newer three-part Windows
Installer version and the expected versioned x64 MSI asset name. The download
is size-limited and restricted to HTTPS GitHub release asset hosts. Installation
requires Windows Authenticode validation for the Akatsukisoft publisher, a
currently valid code-signing chain trusted by the machine, and
matching MSI product, manufacturer, upgrade code and version. Releases must
publish a signed MSI with that publisher identity to support automatic updates.

The worker keeps the verified file open for reading so it cannot be replaced,
waits for the host to exit, and uses Windows Installer with the existing
installation directory and intended user SID. Windows requests elevation when
required. The worker starts Githubie under the original user after Windows
Installer exits and skips the startup update on that restart. The settings
window displays the result. If Windows Installer requires a reboot, it is left
to the user. A ZIP installation can check for releases from the settings window.
