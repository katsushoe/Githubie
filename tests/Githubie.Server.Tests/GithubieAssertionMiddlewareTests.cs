using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moyai.ProviderAuthentication;
using Xunit;

namespace Githubie.Server.Tests;

public sealed class GithubieAssertionMiddlewareTests
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Theory]
    [InlineData("alias", StatusCodes.Status403Forbidden)]
    [InlineData("project", StatusCodes.Status403Forbidden)]
    [InlineData("repository", StatusCodes.Status403Forbidden)]
    [InlineData("scope", StatusCodes.Status403Forbidden)]
    [InlineData("expired", StatusCodes.Status401Unauthorized)]
    [InlineData("missing", StatusCodes.Status401Unauthorized)]
    [InlineData("legacy", StatusCodes.Status401Unauthorized)]
    public async Task InvokeAsync_InvalidIdentityOrCredential_DoesNotDispatch(string failure, int expectedStatus)
    {
        using var fixture = new AssertionFixture();
        var assertion = failure switch
        {
            "alias" => fixture.CreateAssertion(audience: "githubbie"),
            "project" => fixture.CreateAssertion(project: Guid.Parse("01901783-394f-4708-8279-353abe36a42d")),
            "repository" => fixture.CreateAssertion(repository: "github.com/example-org/another-repo"),
            "scope" => fixture.CreateAssertion(scope: "repository.read"),
            "expired" => fixture.CreateAssertion(issuedOffsetSeconds: -121),
            "missing" => null,
            "legacy" => "test-legacy-service-token",
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };
        var dispatched = 0;
        var context = fixture.CreateRequest(assertion);

        await CreateMiddleware(() => dispatched++).InvokeAsync(context, fixture.Options, fixture.Repositories,
            fixture.Validator, fixture.Validator, fixture.ExecutionContext);

        dispatched.Should().Be(0);
        context.Response.StatusCode.Should().Be(expectedStatus);
    }

    [Fact]
    public async Task InvokeAsync_ExpiresDuringWait_RejectsWithoutReservingReplayAgain()
    {
        using var fixture = new AssertionFixture();
        var dispatched = 0;
        var middleware = new GithubieAssertionMiddleware(async _ =>
        {
            fixture.Advance(TimeSpan.FromSeconds(121));
            await fixture.ExecutionContext.EnsureCurrentAsync();
            dispatched++;
        }, NullLogger<GithubieAssertionMiddleware>.Instance);
        var context = fixture.CreateRequest(fixture.CreateAssertion());

        await middleware.InvokeAsync(context, fixture.Options, fixture.Repositories,
            fixture.Validator, fixture.Validator, fixture.ExecutionContext);

        dispatched.Should().Be(0);
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        fixture.ReplayReservations.Should().Be(1);
    }

    [Fact]
    public async Task InvokeAsync_ValidAssertionDispatchesToGateway()
    {
        using var fixture = new AssertionFixture();
        var dispatched = 0;
        var middleware = CreateMiddleware(() => dispatched++);
        var context = fixture.CreateRequest(fixture.CreateAssertion());

        await middleware.InvokeAsync(context, fixture.Options, fixture.Repositories, fixture.Validator, fixture.Validator, fixture.ExecutionContext);

        dispatched.Should().Be(1);
    }

    [Fact]
    public async Task InvokeAsync_CapabilityRemovedDuringWait_RejectsWithoutReservingReplayAgain()
    {
        using var fixture = new AssertionFixture();
        var scopes = new Dictionary<string, string[]> { ["github_push"] = ["repository.push"] };
        var executionValidator = fixture.CreateExecutionValidator(scopes);
        var dispatched = 0;
        var middleware = new GithubieAssertionMiddleware(async _ =>
        {
            scopes.Clear();
            await fixture.ExecutionContext.EnsureCurrentAsync();
            dispatched++;
        }, NullLogger<GithubieAssertionMiddleware>.Instance);
        var context = fixture.CreateRequest(fixture.CreateAssertion());

        await middleware.InvokeAsync(context, fixture.Options, fixture.Repositories,
            fixture.Validator, executionValidator, fixture.ExecutionContext);

        dispatched.Should().Be(0);
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        fixture.ReplayReservations.Should().Be(1);
    }

    [Fact]
    public async Task InvokeAsync_WrongAudienceDoesNotDispatchToGateway()
    {
        using var fixture = new AssertionFixture();
        var dispatched = 0;
        var middleware = CreateMiddleware(() => dispatched++);
        var context = fixture.CreateRequest(fixture.CreateAssertion(audience: "buckettie"));

        await middleware.InvokeAsync(context, fixture.Options, fixture.Repositories, fixture.Validator, fixture.Validator, fixture.ExecutionContext);

        dispatched.Should().Be(0);
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task InvokeAsync_ReplayedAssertionDoesNotDispatchTwice()
    {
        using var fixture = new AssertionFixture();
        var dispatched = 0;
        var middleware = CreateMiddleware(() => dispatched++);
        var assertion = fixture.CreateAssertion();

        await middleware.InvokeAsync(fixture.CreateRequest(assertion), fixture.Options, fixture.Repositories, fixture.Validator, fixture.Validator, fixture.ExecutionContext);
        var replay = fixture.CreateRequest(assertion);
        await middleware.InvokeAsync(replay, fixture.Options, fixture.Repositories, fixture.Validator, fixture.Validator, fixture.ExecutionContext);

        dispatched.Should().Be(1);
        replay.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_ExecutionGuardRejectionDoesNotDispatchToGateway()
    {
        using var fixture = new AssertionFixture();
        var dispatched = 0;
        var middleware = CreateMiddleware(() => dispatched++);
        var context = fixture.CreateRequest(fixture.CreateAssertion());

        await middleware.InvokeAsync(
            context,
            fixture.Options,
            fixture.Repositories,
            fixture.Validator,
            new RejectingExecutionValidator(),
            fixture.ExecutionContext);

        dispatched.Should().Be(0);
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_DirectLocalCallWithoutMoyaiHeaders_DispatchesWithoutPrincipal()
    {
        using var fixture = new AssertionFixture();
        var dispatched = 0;
        var context = fixture.CreateRequest(assertion: null, moyaiRequest: false);

        await CreateMiddleware(() => dispatched++).InvokeAsync(context, fixture.Options, fixture.Repositories,
            fixture.Validator, fixture.Validator, fixture.ExecutionContext);

        dispatched.Should().Be(1);
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_DirectLocalCallWhenAssertionRequired_IsRejected()
    {
        using var fixture = new AssertionFixture();
        var options = fixture.Options with
        {
            ProviderAuthentication = fixture.Options.ProviderAuthentication! with { RequireAssertion = true },
        };
        var dispatched = 0;
        var context = fixture.CreateRequest(assertion: null, moyaiRequest: false);

        await CreateMiddleware(() => dispatched++).InvokeAsync(context, options, fixture.Repositories,
            fixture.Validator, fixture.Validator, fixture.ExecutionContext);

        dispatched.Should().Be(0);
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_InvalidAssertionWithoutOperationId_DoesNotFallBackToDirectCall()
    {
        using var fixture = new AssertionFixture();
        var dispatched = 0;
        var context = fixture.CreateRequest("test-legacy-service-token", moyaiRequest: false);

        await CreateMiddleware(() => dispatched++).InvokeAsync(context, fixture.Options, fixture.Repositories,
            fixture.Validator, fixture.Validator, fixture.ExecutionContext);

        dispatched.Should().Be(0);
        context.Response.StatusCode.Should().NotBe(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_ManagementToolRemainsAvailableForBootstrap()
    {
        using var fixture = new AssertionFixture();
        var dispatched = 0;
        var middleware = CreateMiddleware(() => dispatched++);
        var context = fixture.CreateRequest(assertion: null, tool: "get_version", includeRepository: false);

        await middleware.InvokeAsync(context, fixture.Options, fixture.Repositories, fixture.Validator, fixture.Validator, fixture.ExecutionContext);

        dispatched.Should().Be(1);
    }

    [Fact]
    public async Task InvokeAsync_FileTrustAndSqliteReplayRejectAcrossValidatorRestart()
    {
        using var fixture = new AssertionFixture();
        var directory = Path.Combine(Path.GetTempPath(), "githubie-assertion-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dispatched = 0;
            var middleware = CreateMiddleware(() => dispatched++);
            var assertion = fixture.CreateAssertion();
            var first = await fixture.CreateConcreteValidatorAsync(directory, SigningKeyState.Active);
            await middleware.InvokeAsync(
                fixture.CreateRequest(assertion), fixture.Options, fixture.Repositories,
                first, first, fixture.ExecutionContext);

            var restarted = await fixture.CreateConcreteValidatorAsync(directory, SigningKeyState.Active);
            var replay = fixture.CreateRequest(assertion);
            await middleware.InvokeAsync(
                replay, fixture.Options, fixture.Repositories,
                restarted, restarted, fixture.ExecutionContext);

            dispatched.Should().Be(1);
            replay.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InvokeAsync_TrustRevokedDuringWaitDoesNotDispatchToGateway()
    {
        using var fixture = new AssertionFixture();
        var directory = Path.Combine(Path.GetTempPath(), "githubie-assertion-" + Guid.NewGuid().ToString("N"));
        try
        {
            var validator = await fixture.CreateConcreteValidatorAsync(directory, SigningKeyState.Active);
            var dispatched = 0;
            var middleware = new GithubieAssertionMiddleware(async _ =>
            {
                await fixture.WriteTrustAsync(directory, SigningKeyState.Revoked);
                await fixture.ExecutionContext.EnsureCurrentAsync();
                dispatched++;
            }, NullLogger<GithubieAssertionMiddleware>.Instance);
            var context = fixture.CreateRequest(fixture.CreateAssertion());

            await middleware.InvokeAsync(
                context, fixture.Options, fixture.Repositories,
                validator, validator, fixture.ExecutionContext);

            dispatched.Should().Be(0);
            context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static GithubieAssertionMiddleware CreateMiddleware(Action dispatch) =>
        new(_ =>
        {
            dispatch();
            return Task.CompletedTask;
        }, NullLogger<GithubieAssertionMiddleware>.Instance);

    private sealed class AssertionFixture : IDisposable
    {
        private readonly TestClock _clock = new();
        private readonly ECDsa _signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly AssertionTrustKey _key;
        private readonly TestReplayCache _replay = new();

        public int ReplayReservations => _replay.Reservations;

        public void Advance(TimeSpan duration) => _clock.Now += duration;

        public Es256AssertionValidator CreateExecutionValidator(IReadOnlyDictionary<string, string[]> scopes) =>
            new(new TestTrustStore(_key), _replay, new AssertionOptions(_key.Issuer, ClockSkewSeconds: 0),
                new AssertionCapability("githubie", "githubie", "1", "ES256", true, scopes), _clock);

        public AssertionFixture()
        {
            var publicKey = _signer.ExportParameters(false);
            _key = new AssertionTrustKey(
                "moyai:test",
                "test-key",
                "ES256",
                Encode(publicKey.Q.X!),
                Encode(publicKey.Q.Y!),
                _clock.Now.AddDays(-1),
                _clock.Now.AddDays(1),
                SigningKeyState.Active);
            Validator = new Es256AssertionValidator(
                new TestTrustStore(_key),
                _replay,
                new AssertionOptions(_key.Issuer, ClockSkewSeconds: 0),
                GithubieAssertionPolicy.CreateCapability(),
                _clock);
        }

        public Es256AssertionValidator Validator { get; }

        public GithubieAssertionExecutionContext ExecutionContext { get; } = new();

        public GithubieOptions Options { get; } = new(
            GithubieOptions.DefaultMcpPort,
            GithubieOptions.DefaultMcpPath,
            new Dictionary<string, RepositoryOptions> { ["example"] = CreateRepository() })
        {
            ProviderAuthentication = new(
                "moyai:test",
                "C:\\Githubie\\config\\moyai-trust.json",
                "C:\\Githubie\\data\\moyai-replay.db",
                new Dictionary<string, Guid> { ["example"] = ProjectId }),
        };

        public RepositoryAllowlist Repositories { get; } =
            new(new Dictionary<string, RepositoryOptions> { ["example"] = CreateRepository() });

        public DefaultHttpContext CreateRequest(
            string? assertion,
            string tool = "github_push",
            bool includeRepository = true,
            bool moyaiRequest = true)
        {
            var arguments = includeRepository ? new { repository = "example" } : (object)new { };
            var body = JsonSerializer.SerializeToUtf8Bytes(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/call",
                @params = new { name = tool, arguments },
            });
            var context = new DefaultHttpContext();
            context.Request.Path = GithubieOptions.DefaultMcpPath;
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = body.Length;
            context.Request.Body = new MemoryStream(body);
            if (moyaiRequest) context.Request.Headers["X-Moyai-Operation-Id"] = "test-operation";
            if (assertion is not null)
            {
                context.Request.Headers.Authorization = $"Bearer {assertion}";
            }

            return context;
        }

        public async Task<Es256AssertionValidator> CreateConcreteValidatorAsync(
            string directory,
            SigningKeyState state)
        {
            var trustPath = await WriteTrustAsync(directory, state);
            var replay = new SqliteAssertionReplayCache(
                new SqliteAssertionReplayCacheOptions(Path.Combine(directory, "replay.db")),
                _clock);
            await replay.InitializeAsync();
            return new Es256AssertionValidator(
                new FileAssertionTrustStore(trustPath),
                replay,
                new AssertionOptions(_key.Issuer, ClockSkewSeconds: 0),
                GithubieAssertionPolicy.CreateCapability(),
                _clock);
        }

        public async Task<string> WriteTrustAsync(string directory, SigningKeyState state)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "trust.json");
            await File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(new[] { _key with { Status = state } }, AssertionJson.Options));
            return path;
        }

        public string CreateAssertion(string audience = GithubieAssertionPolicy.ProviderId,
            Guid? project = null, string repository = "github.com/example-org/example-repo",
            string scope = "repository.push", int issuedOffsetSeconds = 0)
        {
            var issued = _clock.Now.ToUnixTimeSeconds() + issuedOffsetSeconds;
            var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new
            {
                typ = "JWT",
                alg = "ES256",
                kid = _key.Kid,
            }));
            var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(new
            {
                iss = _key.Issuer,
                sub = _key.Issuer,
                aud = audience,
                prv = audience,
                iat = issued,
                nbf = issued,
                exp = issued + 120,
                jti = Guid.NewGuid().ToString("N"),
                project = (project ?? ProjectId).ToString("D"),
                repository,
                scope = new[] { scope },
                protocol_version = GithubieAssertionPolicy.ProtocolVersion,
                operation_id = "test-operation",
            }));
            var unsigned = $"{header}.{payload}";
            var signature = _signer.SignData(
                Encoding.ASCII.GetBytes(unsigned),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return $"{unsigned}.{Encode(signature)}";
        }

        public void Dispose() => _signer.Dispose();

        private static RepositoryOptions CreateRepository() => new(
            "example-org",
            "example-repo",
            "D:\\Projects\\Example",
            "origin",
            "develop",
            "main",
            ["develop"],
            ["develop", "main"],
            ["main"],
            "main",
            "^v[0-9]+\\.[0-9]+\\.[0-9]+$",
            "merge",
            true);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TestTrustStore(AssertionTrustKey key) : IAssertionTrustStore
    {
        public Task<AssertionTrustKey?> FindAsync(
            string issuer,
            string keyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AssertionTrustKey?>(key);
    }

    private sealed class TestReplayCache : IAssertionReplayCache
    {
        private readonly HashSet<string> _used = new(StringComparer.Ordinal);
        public int Reservations { get; private set; }

        public Task<bool> TryUseAsync(
            string issuer,
            string jti,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default)
        {
            Reservations++;
            return Task.FromResult(_used.Add($"{issuer}\n{jti}"));
        }
    }

    private sealed class RejectingExecutionValidator : IAssertionExecutionValidator
    {
        public Task EnsureCurrentAsync(
            AssertionPrincipal principal,
            CancellationToken cancellationToken = default) =>
            throw new ProviderAuthenticationException("auth_assertion_expired");
    }

    private static string Encode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
