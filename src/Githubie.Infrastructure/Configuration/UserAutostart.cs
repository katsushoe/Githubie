using System.Runtime.Versioning;
using System.Text.Json;
using Githubie.Application.Configuration;
using Microsoft.Win32;

namespace Githubie.Infrastructure.Configuration;

/// <summary>現在ユーザーのログオン起動を変更し、OSに保存した値を読み戻します。</summary>
[SupportedOSPlatform("windows")]
public sealed class UserAutostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "Githubie.UserHost";
    private readonly string _preferencePath;
    private readonly string _command;
    private readonly IUserStartupRegistration _registration;

    public UserAutostart(string hostExecutablePath, string? preferencePath = null, IUserStartupRegistration? registration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostExecutablePath);
        _preferencePath = preferencePath ?? GithubieConfigurationPath.Resolve(null,
            Path.GetDirectoryName(Path.GetFullPath(hostExecutablePath))!);
        _command = $"\"{Path.GetFullPath(hostExecutablePath)}\" --user-host --autostart";
        _registration = registration ?? new CurrentUserRegistration();
    }

    public bool IsEnabled()
    {
        return string.Equals(_registration.Read(), _command, StringComparison.Ordinal);
    }

    public bool Initialize()
    {
        return ApplyRegistration(new UserHostSettingsStore(_preferencePath).Read().AutostartEnabled);
    }

    public bool SetEnabled(bool enabled)
    {
        using var preferenceLock = new FileStream(_preferencePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        return SetEnabledCore(enabled);
    }

    private bool SetEnabledCore(bool enabled)
    {
        var existing = _registration.Read();
        if (existing is not null && !string.Equals(existing, _command, StringComparison.Ordinal))
            throw new IOException("A different installation owns the Githubie startup entry.");
        var temporary = new UserHostSettingsStore(_preferencePath).PrepareAutostart(enabled);
        try
        {
            _registration.Write(enabled ? _command : null);
            if (IsEnabled() != enabled) throw new IOException("Startup registration could not be verified.");
            File.Replace(temporary, _preferencePath, null);
        }
        catch
        {
            _registration.Write(existing);
            throw;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return enabled;
    }

    private bool ApplyRegistration(bool enabled)
    {
        var existing = _registration.Read();
        if (existing is not null && !string.Equals(existing, _command, StringComparison.Ordinal))
            throw new IOException("A different installation owns the Githubie startup entry.");
        try
        {
            _registration.Write(enabled ? _command : null);
            if (IsEnabled() != enabled) throw new IOException("Startup registration could not be verified.");
            return enabled;
        }
        catch { _registration.Write(existing); throw; }
    }

    private sealed class CurrentUserRegistration : IUserStartupRegistration
    {
        public string? Read()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(EntryName) as string;
        }

        public void Write(string? command)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (command is null) key.DeleteValue(EntryName, throwOnMissingValue: false);
            else key.SetValue(EntryName, command, RegistryValueKind.String);
        }
    }
}
