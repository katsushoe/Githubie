using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.Credentials;
using Githubie.Infrastructure.Configuration;
using Githubie.Infrastructure.Credentials;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Githubie.Infrastructure.Tests;

public sealed class SqliteUserCredentialStoreTests
{
    private static RepositoryOptions Options(string name) => new("owner", name, @"C:\repos\sample", "origin", "develop", "main",
        ["develop"], ["develop", "main"], ["main"], "main", "^v.*$", "merge", true);

    [Fact]
    public async Task Registration_CommitsSettingsAndEncryptedTokenTogether()
    {
        await WithStoreAsync(async (path, settings, tokens) =>
        {
            await tokens.SaveRegistrationAsync("new", Options("new"), "test-secret".ToCharArray(), TestContext.Current.CancellationToken);
            (await settings.GetRepositoryAsync("new", TestContext.Current.CancellationToken))!.GitHubRepo.Should().Be("new");
            var read = tokens.Read("new");
            read.IsSuccess.Should().BeTrue();
            new string(read.Token!).Should().Be("test-secret");
            Array.Clear(read.Token!);
            Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)).Should().NotContain("test-secret");
        });
    }

    [Fact]
    public async Task CredentialInsertFailure_RollsBackNewSettingsAndPreservesExistingRegistration()
    {
        await WithStoreAsync(async (path, settings, tokens) =>
        {
            await tokens.SaveRegistrationAsync("existing", Options("original"), "original-secret".ToCharArray(), TestContext.Current.CancellationToken);
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var trigger = connection.CreateCommand();
                trigger.CommandText = "CREATE TRIGGER fail_new BEFORE INSERT ON user_credentials WHEN NEW.repository_id = 'new' BEGIN SELECT RAISE(ABORT, 'injected failure'); END;";
                trigger.ExecuteNonQuery();
            }
            var save = () => tokens.SaveRegistrationAsync("new", Options("new"), "new-secret".ToCharArray(), TestContext.Current.CancellationToken);
            await save.Should().ThrowAsync<IOException>();
            (await settings.GetRepositoryAsync("new", TestContext.Current.CancellationToken)).Should().BeNull();
            tokens.Read("new").Error.Should().Be(ApiTokenStoreError.TokenNotFound);
            (await settings.GetRepositoryAsync("existing", TestContext.Current.CancellationToken))!.GitHubRepo.Should().Be("original");
            new string(tokens.Read("existing").Token!).Should().Be("original-secret");
        });
    }

    [Fact]
    public async Task DuplicateId_DoesNotOverwriteSettingsOrCredential()
    {
        await WithStoreAsync(async (_, settings, tokens) =>
        {
            await tokens.SaveRegistrationAsync("existing", Options("original"), "original".ToCharArray(), TestContext.Current.CancellationToken);
            var duplicate = () => tokens.SaveRegistrationAsync("existing", Options("changed"), "changed".ToCharArray(), TestContext.Current.CancellationToken);
            await duplicate.Should().ThrowAsync<IOException>();
            (await settings.GetRepositoryAsync("existing", TestContext.Current.CancellationToken))!.GitHubRepo.Should().Be("original");
            new string(tokens.Read("existing").Token!).Should().Be("original");
        });
    }

    [Fact]
    public async Task ExplicitSkip_CommitsOnlySettings()
    {
        await WithStoreAsync(async (_, settings, tokens) =>
        {
            await tokens.SaveRegistrationAsync("local", Options("local"), null, TestContext.Current.CancellationToken);
            (await settings.GetRepositoryAsync("local", TestContext.Current.CancellationToken)).Should().NotBeNull();
            tokens.Read("local").Error.Should().Be(ApiTokenStoreError.TokenNotFound);
        });
    }

    [Fact]
    public async Task EncryptionFailure_LeavesNoRegistration()
    {
        await WithStoreAsync(async (path, settings, _) =>
        {
            var failing = new SqliteUserCredentialStore(path, new FailingProtector());
            var save = () => failing.SaveRegistrationAsync("new", Options("new"), "secret".ToCharArray(), TestContext.Current.CancellationToken);
            await save.Should().ThrowAsync<IOException>();
            (await settings.GetRepositoryAsync("new", TestContext.Current.CancellationToken)).Should().BeNull();
        });
    }

    [Fact]
    public async Task RenameCredentialFailure_RollsBackBothNames()
    {
        await WithStoreAsync(async (path, settings, tokens) =>
        {
            await tokens.SaveRegistrationAsync("existing", Options("original"), "original-secret".ToCharArray(), TestContext.Current.CancellationToken);
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var trigger = connection.CreateCommand();
                trigger.CommandText = "CREATE TRIGGER fail_rename BEFORE UPDATE ON user_credentials BEGIN SELECT RAISE(ABORT, 'injected failure'); END;";
                trigger.ExecuteNonQuery();
            }
            var rename = () => tokens.RenameRegistrationAsync("existing", "new", TestContext.Current.CancellationToken);
            await rename.Should().ThrowAsync<IOException>();
            (await settings.GetRepositoryAsync("existing", TestContext.Current.CancellationToken)).Should().NotBeNull();
            (await settings.GetRepositoryAsync("new", TestContext.Current.CancellationToken)).Should().BeNull();
            new string(tokens.Read("existing").Token!).Should().Be("original-secret");
            tokens.Read("new").Error.Should().Be(ApiTokenStoreError.TokenNotFound);
        });
    }

    [Fact]
    public async Task DeleteCredentialFailure_RollsBackSettings()
    {
        await WithStoreAsync(async (path, settings, tokens) =>
        {
            await tokens.SaveRegistrationAsync("existing", Options("original"), "original-secret".ToCharArray(), TestContext.Current.CancellationToken);
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var trigger = connection.CreateCommand();
                trigger.CommandText = "CREATE TRIGGER fail_delete BEFORE DELETE ON user_credentials BEGIN SELECT RAISE(ABORT, 'injected failure'); END;";
                trigger.ExecuteNonQuery();
            }
            var delete = () => tokens.DeleteRegistrationAsync("existing", TestContext.Current.CancellationToken);
            await delete.Should().ThrowAsync<IOException>();
            (await settings.GetRepositoryAsync("existing", TestContext.Current.CancellationToken)).Should().NotBeNull();
            tokens.Read("existing").IsSuccess.Should().BeTrue();
        });
    }

    [Fact]
    public async Task RenameAndDelete_CommitCredentialAndSettingsTogether()
    {
        await WithStoreAsync(async (_, settings, tokens) =>
        {
            await tokens.SaveRegistrationAsync("old", Options("original"), "secret".ToCharArray(), TestContext.Current.CancellationToken);
            await tokens.RenameRegistrationAsync("old", "new", TestContext.Current.CancellationToken);
            (await settings.GetRepositoryAsync("old", TestContext.Current.CancellationToken)).Should().BeNull();
            (await settings.GetRepositoryAsync("new", TestContext.Current.CancellationToken)).Should().NotBeNull();
            tokens.Read("old").Error.Should().Be(ApiTokenStoreError.TokenNotFound);
            new string(tokens.Read("new").Token!).Should().Be("secret");
            await tokens.DeleteRegistrationAsync("new", TestContext.Current.CancellationToken);
            (await settings.GetRepositoryAsync("new", TestContext.Current.CancellationToken)).Should().BeNull();
            tokens.Read("new").Error.Should().Be(ApiTokenStoreError.TokenNotFound);
        });
    }

    [Fact]
    public void UserLayout_KeepsBinariesSeparateFromAllMutableDirectories()
    {
        var layout = GithubiePathLayout.FromUserProfileRoot(@"C:\Githubie\bin", @"C:\Users\test\AppData\Local\Akatsukisoft\Githubie");
        layout.InstallRoot.Should().Be(@"C:\Githubie");
        foreach (var path in new[] { layout.ConfigDirectory, layout.DataDirectory, layout.LogsDirectory, layout.SecretsDirectory, layout.ServiceStatePath })
            path.Should().StartWith(@"C:\Users\test\AppData\Local\Akatsukisoft\Githubie\");
    }

    [Fact]
    public void CurrentUserProtector_RoundTripsNewFormatAndRejectsRawLegacyBlob()
    {
        if (!OperatingSystem.IsWindows()) return;
        var protector = new CurrentUserDpapiProtector();
        var plain = Encoding.UTF8.GetBytes("scope-test");
        var entropy = Encoding.UTF8.GetBytes("Githubie-test-entropy");
        var encrypted = protector.Protect(plain, entropy);
        protector.Unprotect(encrypted, entropy).Should().Equal(plain);
        var machine = ProtectedData.Protect(plain, entropy, DataProtectionScope.LocalMachine);
        Action decrypt = () => { if (OperatingSystem.IsWindows()) protector.Unprotect(machine, entropy); };
        decrypt.Should().Throw<CryptographicException>();
    }

    private static async Task WithStoreAsync(Func<string, SqliteRepositoryConfigurationStore, SqliteUserCredentialStore, Task> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "Githubie-user-transaction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "githubie.db");
            var settings = new SqliteRepositoryConfigurationStore(path);
            await settings.InitializeAsync(new Dictionary<string, RepositoryOptions>(), TestContext.Current.CancellationToken);
            await action(path, settings, new SqliteUserCredentialStore(path, new TestProtector()));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // Deterministic transport test only. Production always uses CurrentUserDpapiProtector.
    private sealed class TestProtector : IDpapiProtector
    {
        public byte[] Protect(byte[] bytes, byte[] entropy) => bytes.Select(value => (byte)(value ^ 0x5a)).ToArray();
        public byte[] Unprotect(byte[] bytes, byte[] entropy) => Protect(bytes, entropy);
    }
    private sealed class FailingProtector : IDpapiProtector
    {
        public byte[] Protect(byte[] bytes, byte[] entropy) => throw new CryptographicException("Injected failure");
        public byte[] Unprotect(byte[] bytes, byte[] entropy) => throw new CryptographicException("Injected failure");
    }
}
