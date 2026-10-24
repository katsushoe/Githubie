namespace Githubie.Cli;

internal static class ClientRegistrationCli
{
    public static async Task<int> RunAsync(string client, bool configure, bool replaceLegacy,
        TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (client is not ("codex" or "claude")) { error.WriteLine("[NG] Supported clients: codex, claude."); return 1; }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // These are the AI client's documented configuration locations, not Githubie service configuration.
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        var path = client == "codex" ? Path.Combine(string.IsNullOrWhiteSpace(codexHome) ? Path.Combine(home, ".codex") : codexHome, "config.toml")
            : Path.Combine(home, ".claude.json");
        if (configure && !File.Exists(path) && !ClientAvailable(client))
        { error.WriteLine("[NG] AI client is not installed or its user configuration is unavailable."); return 1; }
        try
        {
            var changed = await new ClientRegistration(Path.Combine(AppContext.BaseDirectory, "githubie.exe"))
                .ApplyAsync(client, path, configure, replaceLegacy, cancellationToken);
            output.WriteLine(changed ? "changed" : "unchanged");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
            or System.Text.Json.JsonException or InvalidOperationException)
        {
            error.WriteLine($"[NG] Client configuration could not be updated ({exception.GetType().Name}). Check the existing githubie entry, permissions and other writers. Use --replace-legacy only for the documented legacy loopback entry.");
            return 1;
        }
    }

    private static bool ClientAvailable(string client)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            foreach (var extension in new[] { ".exe", ".cmd", ".bat", ".ps1" })
                if (File.Exists(Path.Combine(directory.Trim('"'), client + extension))) return true;
        }
        return false;
    }
}
