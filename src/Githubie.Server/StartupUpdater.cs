using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Githubie.Server;

internal sealed class StartupUpdater(string executable, string[] hostArguments)
{
    private const string ReleaseRepository = "katsushoe/Githubie";
    private const string ReleaseEndpoint = "https://api.github.com/repos/" + ReleaseRepository + "/releases/latest";
    private const long MaximumPackageBytes = 256L * 1024 * 1024;
    internal const string SkipEnvironmentVariable = "GITHUBIE_SKIP_STARTUP_UPDATE";

    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    public static Version CurrentVersion => typeof(StartupUpdater).Assembly.GetName().Version!;

    public async Task<string> PrepareAsync(Func<bool>? canInstall, CancellationToken cancellationToken)
    {
        var installRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, ".."));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        var token = deadline.Token;
        try
        {
            if (canInstall is not null && !IsInstalledMsi(installRoot)) return "UpdateRequiresMsi";
            using var request = Request(new Uri(ReleaseEndpoint));
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode == HttpStatusCode.NotFound) return "UpdateSourceUnavailable";
            response.EnsureSuccessStatusCode();
            await using var release = await response.Content.ReadAsStreamAsync(token);
            using var metadata = new MemoryStream();
            await CopyBoundedAsync(release, metadata, 1024 * 1024, token);
            using var document = JsonDocument.Parse(metadata.ToArray());
            var package = FindPackage(document.RootElement, CurrentVersion);
            if (package is null) return "UpdateNone";
            if (canInstall is null) return "UpdateAvailable";
            return await StageAsync(package, installRoot, canInstall, token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return "UpdateFailed";
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or
            InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or Win32Exception or
            System.Security.SecurityException or ArgumentException)
        {
            Console.Error.WriteLine($"Githubie startup update: {exception.GetType().Name}");
            return "UpdateFailed";
        }
    }

    internal sealed record Package(Version Version, string FileName, Uri Uri);

    internal static Package? FindPackage(JsonElement release, Version currentVersion)
    {
        if (release.ValueKind != JsonValueKind.Object ||
            !release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
            !release.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind != JsonValueKind.False ||
            !release.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String ||
            !release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        var tagName = tag.GetString()!;
        var versionText = tagName.StartsWith('v') ? tagName[1..] : tagName;
        if (!Version.TryParse(versionText, out var version) || version.Revision < 0 ||
            versionText != version.ToString() || version.Major > 255 || version.Minor > 255 ||
            version.Build > 65535 ||
            new Version(version.Major, version.Minor, version.Build) <=
            new Version(currentVersion.Major, currentVersion.Minor, Math.Max(0, currentVersion.Build)))
            return null;

        var fileName = $"Githubie-{version}-win-x64.msi";
        var expected = $"https://github.com/{ReleaseRepository}/releases/download/{tagName}/{fileName}";
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind == JsonValueKind.Object &&
                asset.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String &&
                name.GetString() == fileName &&
                asset.TryGetProperty("browser_download_url", out var url) && url.ValueKind == JsonValueKind.String &&
                url.GetString() == expected)
                return new(version, fileName, new Uri(expected));
        }

        return null;
    }

    private static bool IsInstalledMsi(string root)
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.OpenSubKey(@"SOFTWARE\Akatsukisoft\Githubie");
        return key?.GetValue("InstallRoot") is string registered &&
            Path.IsPathFullyQualified(registered) &&
            string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(registered)),
                Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> StageAsync(Package package, string installRoot, Func<bool> canInstall, CancellationToken token)
    {
        var folder = Directory.CreateTempSubdirectory("Githubie-Update-");
        var handedOff = false;
        try
        {
            var path = Path.Combine(folder.FullName, package.FileName);
            await DownloadAsync(package.Uri, path, token);
            token.ThrowIfCancellationRequested();

            var ready = Path.Combine(folder.FullName, "verified");
            var commit = Path.Combine(folder.FullName, "apply");
            var helper = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            using var script = Assembly.GetExecutingAssembly().GetManifestResourceStream("Githubie.Server.ApplyStartupUpdate.ps1")
                ?? throw new InvalidOperationException("The update script is missing.");
            using var reader = new StreamReader(script);
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(await reader.ReadToEndAsync(token)));
            using var owner = Process.GetCurrentProcess();
            var launch = new ProcessStartInfo(helper)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Environment.SystemDirectory
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-STA", "-EncodedCommand", encoded })
                launch.ArgumentList.Add(argument);
            launch.Environment["GITHUBIE_UPDATE_INPUT"] = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new
            {
                path,
                ready,
                commit,
                installRoot,
                executable,
                hostArguments,
                version = package.Version.ToString(3),
                owner = owner.Id,
                ownerStart = owner.StartTime.ToUniversalTime().ToFileTimeUtc().ToString(CultureInfo.InvariantCulture)
            }));
            using var worker = Process.Start(launch) ?? throw new IOException("The update worker could not start.");
            while (!File.Exists(ready))
            {
                if (worker.HasExited) return "UpdateRejected";
                await Task.Delay(TimeSpan.FromMilliseconds(200), token);
            }
            token.ThrowIfCancellationRequested();
            if (!canInstall()) return "UpdateIdle";
            File.WriteAllText(commit, "");
            handedOff = true;
            return "UpdateApplying";
        }
        finally
        {
            if (!handedOff)
            {
                try { folder.Delete(recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static HttpRequestMessage Request(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd($"Githubie/{CurrentVersion}");
        return request;
    }

    private static async Task DownloadAsync(Uri uri, string path, CancellationToken token)
    {
        for (var redirects = 0; redirects <= 4; redirects++)
        {
            using var request = Request(uri);
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location ?? throw new IOException("The release redirect has no destination.");
                uri = new Uri(uri, location);
                if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
                    uri.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                    throw new IOException("The release redirect does not lead to a GitHub asset host.");
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaximumPackageBytes)
                throw new IOException("The update package exceeds the maximum size.");
            await using var download = await response.Content.ReadAsStreamAsync(token);
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await CopyBoundedAsync(download, output, MaximumPackageBytes, token);
            return;
        }
        throw new IOException("The update exceeded the redirect limit.");
    }

    private static async Task CopyBoundedAsync(Stream source, Stream destination, long maximum, CancellationToken token)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, token)) != 0)
        {
            total += read;
            if (total > maximum) throw new IOException("The update exceeded the size limit.");
            await destination.WriteAsync(buffer.AsMemory(0, read), token);
        }
    }
}
