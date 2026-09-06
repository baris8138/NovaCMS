using System.Globalization;
using NovaCMS.Application.Security;
using NovaCMS.Domain.Authentication;
using Xunit;

namespace NovaCMS.Infrastructure.Tests.Security;

// Application orchestration tests: no EF, database, or token/crypto implementation needed.
public sealed class LoginServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(PasswordVerificationResult.Success)]
    [InlineData(PasswordVerificationResult.SuccessRehashNeeded)]
    public async Task ValidCredentials_ReturnBothTokensOnlyAfterPersistence(PasswordVerificationResult verification)
    {
        var fixture = new Fixture(verification);
        var result = await fixture.Service.LoginAsync(new LoginRequest("  USER@example.COM  ", "test-only-password"));
        Assert.Equal(LoginStatus.Success, result.Status);
        Assert.Equal(fixture.Generator.Result.Token, result.AccessToken);
        Assert.Equal(fixture.Generator.Result.ExpiresAt, result.AccessTokenExpiresAt);
        Assert.Equal(fixture.Store.Token.Token, result.RefreshToken);
        Assert.Equal(fixture.Store.Token.ExpiresAt, result.RefreshTokenExpiresAt);
        Assert.Equal("USER@EXAMPLE.COM", fixture.Store.Lookup);
        Assert.Equal(fixture.Store.User!.Id, fixture.Generator.Request!.UserId);
        Assert.Equal(fixture.Store.User.Email, fixture.Generator.Request.Email);
        Assert.Equal(Now, fixture.Store.LoginAt);
        Assert.Equal(1, fixture.Store.Completions);
        Assert.Equal(verification == PasswordVerificationResult.SuccessRehashNeeded ? "fake-hash-2" : null,
            fixture.Store.NewHash);
        Assert.Null(fixture.Store.User.LastLoginAt);
        Assert.Equal("fake-original-hash", fixture.Store.User.PasswordHash);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnknownEmailAndWrongPassword_HaveSameFailureWithoutTokenGeneration(bool unknown)
    {
        var fixture = new Fixture(PasswordVerificationResult.Failed);
        if (unknown) fixture.Store.User = null;
        var result = await fixture.Service.LoginAsync(new LoginRequest("user@example.com", "wrong-test-password"));
        AssertFailure(result);
        Assert.Equal(1, fixture.Hasher.Verifications);
        Assert.Equal(0, fixture.Generator.Calls);
        Assert.Equal(0, fixture.Store.Completions);
    }

    [Fact]
    public async Task UnknownUser_ReusesDummyHashAcrossRequests()
    {
        var fixture = new Fixture();
        fixture.Store.User = null;
        await fixture.Service.LoginAsync(new LoginRequest("missing@example.com", "test-only-password"));
        await fixture.Service.LoginAsync(new LoginRequest("missing@example.com", "test-only-password"));
        Assert.Equal(1, fixture.Hasher.Hashes);
        Assert.Equal(2, fixture.Hasher.Verifications);
        Assert.All(fixture.Hasher.VerifiedHashes, hash => Assert.Equal("fake-hash-1", hash));
        Assert.Equal(0, fixture.Generator.Calls);
    }

    [Fact]
    public async Task InactiveUser_PerformsDummyVerificationWithoutGeneratingTokens()
    {
        var fixture = new Fixture(PasswordVerificationResult.SuccessRehashNeeded);
        // Test-only state setup: the domain currently has no activation/deactivation use-case.
        typeof(User).GetProperty(nameof(User.IsActive))!.SetValue(fixture.Store.User, false);
        AssertFailure(await fixture.Service.LoginAsync(new LoginRequest("user@example.com", "test-only-password")));
        Assert.Equal(new[] { "fake-hash-1" }, fixture.Hasher.VerifiedHashes);
        Assert.Equal(1, fixture.Hasher.Hashes);
        Assert.Equal(0, fixture.Generator.Calls);
        Assert.Equal(0, fixture.Store.Completions);
    }

    [Theory]
    [InlineData(null, "password")]
    [InlineData("", "password")]
    [InlineData("   ", "password")]
    [InlineData("user@example.com", null)]
    [InlineData("user@example.com", "")]
    [InlineData("user@example.com", "   ")]
    public async Task MissingInput_ReturnsFailureWithoutLookup(string? email, string? password)
    {
        var fixture = new Fixture();
        AssertFailure(await fixture.Service.LoginAsync(new LoginRequest(email!, password!)));
        Assert.Null(fixture.Store.Lookup);
        Assert.Equal(0, fixture.Generator.Calls);
        Assert.Equal(0, fixture.Store.Completions);
    }

    [Fact]
    public async Task Normalization_UsesDomainInvariantUnderTurkishCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var fixture = new Fixture();
            const string input = "  identity@example.com  ";
            var domainUser = new User(Guid.NewGuid(), input, "fake-hash", "Test", "User", Now);
            await fixture.Service.LoginAsync(new LoginRequest(input, "test-only-password"));
            Assert.Equal("IDENTITY@EXAMPLE.COM", fixture.Store.Lookup);
            Assert.Equal(domainUser.NormalizedEmail, fixture.Store.Lookup);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task PersistenceFailure_DoesNotReturnLoginResult()
    {
        var fixture = new Fixture();
        fixture.Store.Failure = new InvalidOperationException("Injected persistence failure.");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.LoginAsync(new LoginRequest("user@example.com", "test-only-password")));
        Assert.Equal(1, fixture.Generator.Calls);
    }

    [Fact]
    public async Task TokenGenerationFailure_DoesNotStartPersistence()
    {
        var fixture = new Fixture();
        fixture.Generator.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.LoginAsync(new LoginRequest("user@example.com", "test-only-password")));
        Assert.Equal(0, fixture.Store.Completions);
    }

    [Fact]
    public async Task CredentialsChangedBeforeCommit_ReturnsGenericFailureWithoutTokens()
    {
        var fixture = new Fixture();
        fixture.Store.Reject = true;
        AssertFailure(await fixture.Service.LoginAsync(new LoginRequest("user@example.com", "test-only-password")));
    }

    [Fact]
    public async Task Cancellation_DoesNotStartLookupOrPersistence()
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Service.LoginAsync(
            new LoginRequest("user@example.com", "test-only-password"), cancellation.Token));
        Assert.Null(fixture.Store.Lookup);
        Assert.Equal(0, fixture.Generator.Calls);
    }

    [Fact]
    public async Task NullRequest_ThrowsWithoutSensitiveValues()
    {
        var fixture = new Fixture();
        await Assert.ThrowsAsync<ArgumentNullException>(() => fixture.Service.LoginAsync(null!));
    }

    [Fact]
    public async Task PublicResult_ContainsOnlyDeliveryFieldsAndRedactsToString()
    {
        var fixture = new Fixture();
        var request = new LoginRequest("user@example.com", "test-only-password");
        var result = await fixture.Service.LoginAsync(request);
        Assert.Equal(new[] { "AccessToken", "AccessTokenExpiresAt", "RefreshToken", "RefreshTokenExpiresAt", "Status" },
            typeof(LoginResult).GetProperties().Select(property => property.Name).OrderBy(name => name));
        Assert.DoesNotContain(request.Email, request.ToString());
        Assert.DoesNotContain(request.Password, request.ToString());
        Assert.DoesNotContain(result.AccessToken!, result.ToString());
        Assert.DoesNotContain(result.RefreshToken!, result.ToString());
    }

    private static void AssertFailure(LoginResult result)
    {
        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        Assert.Null(result.AccessToken);
        Assert.Null(result.AccessTokenExpiresAt);
        Assert.Null(result.RefreshToken);
        Assert.Null(result.RefreshTokenExpiresAt);
    }

    private sealed class Fixture
    {
        public StubStore Store { get; } = new();
        public StubHasher Hasher { get; }
        public StubGenerator Generator { get; } = new();
        public LoginService Service { get; }

        public Fixture(PasswordVerificationResult verification = PasswordVerificationResult.Success)
        {
            Hasher = new StubHasher(verification);
            Service = new LoginService(Store, Hasher, Generator, new LoginTimingProtection(Hasher), new FixedTimeProvider());
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StubHasher(PasswordVerificationResult result) : IPasswordHasher
    {
        public int Hashes { get; private set; }
        public int Verifications { get; private set; }
        public List<string> VerifiedHashes { get; } = [];
        public string Hash(string password) => $"fake-hash-{++Hashes}";
        public PasswordVerificationResult Verify(string password, string passwordHash)
        {
            Verifications++;
            VerifiedHashes.Add(passwordHash);
            return result;
        }
    }

    private sealed class StubGenerator : IAccessTokenGenerator
    {
        public AccessTokenResult Result { get; } = new(Guid.NewGuid().ToString("N"), Now.AddMinutes(15));
        public AccessTokenRequest? Request { get; private set; }
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public AccessTokenResult Generate(AccessTokenRequest request)
        {
            Calls++;
            Request = request;
            if (Fail) throw new InvalidOperationException("Injected generator failure.");
            return Result;
        }
    }

    private sealed class StubStore : IAuthenticationUserStore
    {
        public User? User { get; set; } = new(Guid.NewGuid(), "user@example.com", "fake-original-hash", "Test", "User", Now);
        public RefreshTokenResult Token { get; } = new(Guid.NewGuid().ToString("N"), Now.AddDays(7));
        public string? Lookup { get; private set; }
        public string? NewHash { get; private set; }
        public DateTimeOffset LoginAt { get; private set; }
        public int Completions { get; private set; }
        public Exception? Failure { get; set; }
        public bool Reject { get; set; }
        public Task<User?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
        {
            Lookup = normalizedEmail;
            return Task.FromResult(User);
        }
        public Task<RefreshTokenResult?> CompleteLoginAsync(User verifiedUser, string? replacementPasswordHash,
            DateTimeOffset loginAt, CancellationToken cancellationToken = default)
        {
            Completions++;
            NewHash = replacementPasswordHash;
            LoginAt = loginAt;
            if (Failure is not null) return Task.FromException<RefreshTokenResult?>(Failure);
            return Task.FromResult<RefreshTokenResult?>(Reject ? null : Token);
        }
    }
}
