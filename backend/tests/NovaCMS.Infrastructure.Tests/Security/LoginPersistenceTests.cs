using System.Collections.Concurrent;
using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NovaCMS.Application.Security;
using NovaCMS.Domain.Authentication;
using NovaCMS.Infrastructure.Persistence;
using Xunit;

namespace NovaCMS.Infrastructure.Tests.Security;

// Real hasher, JWT generator and shared refresh-token implementation on relational SQLite.
// This does not exercise PostgreSQL-specific MVCC or locking behavior.
public sealed class LoginPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealComponents_LoginCommitsTokensTimestampAndOptionalLegacyRehash(bool legacy)
    {
        using var database = new Database(legacy);
        using var session = database.Open();
        var hasher = session.Provider.GetRequiredService<IPasswordHasher>();
        Assert.Equal(legacy ? PasswordVerificationResult.SuccessRehashNeeded : PasswordVerificationResult.Success,
            hasher.Verify(database.Password, database.OriginalHash));
        var result = await session.Login.LoginAsync(new LoginRequest("  LOGIN@example.COM ", database.Password));
        Assert.Equal(LoginStatus.Success, result.Status);
        Assert.NotEmpty(result.AccessToken!);
        Assert.NotEmpty(result.RefreshToken!);
        Assert.Equal(Now.AddMinutes(15), result.AccessTokenExpiresAt);
        Assert.Equal(Now.AddDays(7), result.RefreshTokenExpiresAt);

        var user = await session.Context.Users.AsNoTracking().SingleAsync();
        var refresh = await session.Context.RefreshTokens.AsNoTracking().SingleAsync();
        Assert.Equal(Now, user.LastLoginAt);
        Assert.Equal(Now, user.UpdatedAt);
        Assert.Equal(PasswordVerificationResult.Success, hasher.Verify(database.Password, user.PasswordHash));
        if (legacy) Assert.NotEqual(database.OriginalHash, user.PasswordHash);
        else Assert.Equal(database.OriginalHash, user.PasswordHash);
        Assert.NotEqual(database.Password, user.PasswordHash);
        Assert.Equal(Hash(result.RefreshToken!), refresh.TokenHash);
        Assert.NotEqual(result.RefreshToken, refresh.TokenHash);
        Assert.Equal(user.Id, refresh.UserId);
        Assert.Equal(Now, refresh.CreatedAt);
        Assert.Equal(result.RefreshTokenExpiresAt, refresh.ExpiresAt);
        Assert.Null(refresh.RevokedAt);

        var handler = new JwtSecurityTokenHandler();
        handler.ValidateToken(result.AccessToken, new TokenValidationParameters
        {
            ValidIssuer = "NovaCMS.LoginTests",
            ValidAudience = "NovaCMS.TestClients",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Database.SigningKey)),
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            LifetimeValidator = (notBefore, expires, _, _) => notBefore <= Now.UtcDateTime && expires > Now.UtcDateTime
        }, out _);
        var jwt = handler.ReadJwtToken(result.AccessToken);
        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.Contains(jwt.Claims, claim => claim.Type == JwtRegisteredClaimNames.Email && claim.Value == user.Email);

        // A token issued through login is usable by the existing lifecycle service, including replay rejection.
        var lifecycle = session.Provider.GetRequiredService<IRefreshTokenService>();
        Assert.Equal(RefreshTokenStatus.Success, (await lifecycle.RotateAsync(result.RefreshToken!)).Status);
        Assert.Equal(RefreshTokenStatus.Revoked, (await lifecycle.RotateAsync(result.RefreshToken!)).Status);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("wrong-password")]
    [InlineData("inactive")]
    public async Task InvalidCredentials_LeaveUserAndTokenStorageUnchanged(string failure)
    {
        using var database = new Database();
        using var session = database.Open();
        if (failure == "inactive")
            await session.Context.Users.ExecuteUpdateAsync(setters => setters.SetProperty(user => user.IsActive, false));
        var result = await session.Login.LoginAsync(new LoginRequest(
            failure == "unknown" ? "unknown@example.com" : "login@example.com",
            failure == "wrong-password" ? "clearly-wrong-test-password" : database.Password));
        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        Assert.Null(result.AccessToken);
        Assert.Null(result.RefreshToken);
        Assert.Null(result.AccessTokenExpiresAt);
        Assert.Null(result.RefreshTokenExpiresAt);
        await AssertOriginalState(session, database);
    }

    [Theory]
    [InlineData("user-update")]
    [InlineData("refresh-insert")]
    [InlineData("commit")]
    public async Task PersistenceFailure_RollsBackRehashTimestampAndRefreshToken(string stage)
    {
        using var database = new Database(legacy: true);
        using (var failing = database.Open(stage == "commit" ? new FailCommit() : new FailCommand(stage)))
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() =>
                failing.Login.LoginAsync(new LoginRequest("login@example.com", database.Password)));
            Assert.DoesNotContain(database.Password, error.ToString());
            Assert.DoesNotContain(database.OriginalHash, error.ToString());
            Assert.DoesNotContain("login@example.com", error.ToString());
            Assert.False(failing.Context.ChangeTracker.HasChanges());
            Assert.Empty(failing.Context.ChangeTracker.Entries<RefreshToken>());
        }
        using var verify = database.Open();
        await AssertOriginalState(verify, database);
        // The same credentials can still succeed after the failed attempt.
        Assert.Equal(LoginStatus.Success,
            (await verify.Login.LoginAsync(new LoginRequest("login@example.com", database.Password))).Status);
    }

    [Fact]
    public async Task LoginResult_IsNotReturnedWhileCommitIsPending()
    {
        using var database = new Database();
        var gate = new BeforeCommitGate();
        using var session = database.Open(gate);
        var pending = session.Login.LoginAsync(new LoginRequest("login@example.com", database.Password));
        await gate.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.False(pending.IsCompleted);
        }
        finally
        {
            gate.Continue.TrySetResult();
        }
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(LoginStatus.Success, result.Status);
        using var verify = database.Open();
        Assert.Equal(Now, (await verify.Context.Users.SingleAsync()).LastLoginAt);
        Assert.Equal(1, await verify.Context.RefreshTokens.CountAsync());
    }

    [Theory]
    [InlineData("password")]
    [InlineData("inactive")]
    [InlineData("email")]
    public async Task Store_RejectsStaleVerificationWithoutIssuingRefreshToken(string change)
    {
        using var database = new Database();
        using var session = database.Open();
        var store = session.Provider.GetRequiredService<IAuthenticationUserStore>();
        var verified = (await store.FindByNormalizedEmailAsync(User.NormalizeEmail("login@example.com")))!;
        using (var other = database.Open())
        {
            if (change == "inactive")
                await other.Context.Users.ExecuteUpdateAsync(setters => setters.SetProperty(user => user.IsActive, false));
            else if (change == "email")
                await other.Context.Users.ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Email, "changed@example.com"));
            else
            {
                var newHash = other.Provider.GetRequiredService<IPasswordHasher>().Hash(Guid.NewGuid().ToString("N"));
                await other.Context.Users.ExecuteUpdateAsync(setters => setters.SetProperty(user => user.PasswordHash, newHash));
            }
        }
        Assert.Null(await store.CompleteLoginAsync(verified, null, Now));
        Assert.Empty(await session.Context.RefreshTokens.ToListAsync());
        Assert.Null((await session.Context.Users.SingleAsync()).LastLoginAt);
    }

    [Fact]
    public async Task TrackedUnchangedUser_IsSupportedAndRefreshedAfterLogin()
    {
        using var database = new Database();
        using var session = database.Open();
        var previous = await session.Context.Users.SingleAsync();
        Assert.Equal(LoginStatus.Success,
            (await session.Login.LoginAsync(new LoginRequest(previous.Email, database.Password))).Status);
        Assert.Equal(EntityState.Detached, session.Context.Entry(previous).State);
        var current = await session.Context.Users.FindAsync(previous.Id);
        Assert.NotSame(previous, current);
        Assert.Equal(Now, current!.LastLoginAt);
        Assert.Equal(0, await session.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task PendingUnrelatedChanges_AreNotCommittedByLogin()
    {
        using var database = new Database();
        using var session = database.Open();
        session.Context.Users.Add(new User(Guid.NewGuid(), "pending@example.com", "fake-test-hash", "Test", "User", Now));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.Login.LoginAsync(new LoginRequest("login@example.com", database.Password)));
        using var verify = database.Open();
        Assert.Equal(1, await verify.Context.Users.CountAsync());
        await AssertOriginalState(verify, database);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Store_RejectsExistingOrAmbientTransaction(bool ambient)
    {
        using var database = new Database();
        using var session = database.Open();
        var store = session.Provider.GetRequiredService<IAuthenticationUserStore>();
        var verified = (await store.FindByNormalizedEmailAsync(User.NormalizeEmail("login@example.com")))!;
        if (ambient)
        {
            using var transaction = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.CompleteLoginAsync(verified, null, Now));
        }
        else
        {
            await using var transaction = await session.Context.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.CompleteLoginAsync(verified, null, Now));
        }
        await AssertOriginalState(session, database);
    }

    [Fact]
    public async Task Security_LogsSqlAndModelsExcludeCredentialsAndPlaintextTokens()
    {
        using var database = new Database(legacy: true);
        var capture = new CaptureCommands();
        using var session = database.Open(capture);
        var result = await session.Login.LoginAsync(new LoginRequest("login@example.com", database.Password));
        var user = await session.Context.Users.AsNoTracking().SingleAsync();
        var logs = string.Join('\n', database.Logs);
        foreach (var secret in new[] { database.Password, database.OriginalHash, user.PasswordHash,
                     result.AccessToken!, result.RefreshToken!, Hash(result.RefreshToken!), "login@example.com", "LOGIN@EXAMPLE.COM" })
            Assert.DoesNotContain(secret, logs);
        Assert.DoesNotContain(database.Password, string.Join('|', capture.Parameters));
        Assert.DoesNotContain(result.RefreshToken!, string.Join('|', capture.Parameters));
        Assert.DoesNotContain(result.AccessToken!, string.Join('|', capture.Parameters));
        Assert.Contains("LOGIN@EXAMPLE.COM", capture.Parameters);
        Assert.Contains(Hash(result.RefreshToken!), capture.Parameters);
        Assert.Contains(capture.Sql, sql => sql.Contains("\"NormalizedEmail\" = @", StringComparison.Ordinal));
        Assert.DoesNotContain(session.Context.Model.FindEntityType(typeof(User))!.GetProperties(),
            property => property.Name == "Password");
        Assert.Equal(new[] { "TokenHash" }, session.Context.Model.FindEntityType(typeof(RefreshToken))!.GetProperties()
            .Where(property => property.ClrType == typeof(string)).Select(property => property.Name));
    }

    private static async Task AssertOriginalState(Session session, Database database)
    {
        var user = await session.Context.Users.AsNoTracking().SingleAsync();
        Assert.Equal(database.OriginalHash, user.PasswordHash);
        Assert.Null(user.LastLoginAt);
        Assert.Equal(Now.AddDays(-1), user.UpdatedAt);
        Assert.Empty(await session.Context.RefreshTokens.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task OlderLoginSnapshot_CannotOverwriteNewerLoginTimestamps()
    {
        using var database = new Database();
        using var older = database.Open();
        using var newer = database.Open();
        var olderStore = older.Provider.GetRequiredService<IAuthenticationUserStore>();
        var newerStore = newer.Provider.GetRequiredService<IAuthenticationUserStore>();
        var normalized = User.NormalizeEmail("login@example.com");
        var oldSnapshot = (await olderStore.FindByNormalizedEmailAsync(normalized))!;
        var newSnapshot = (await newerStore.FindByNormalizedEmailAsync(normalized))!;
        Assert.NotNull(await newerStore.CompleteLoginAsync(newSnapshot, null, Now.AddMinutes(1)));
        Assert.Null(await olderStore.CompleteLoginAsync(oldSnapshot, null, Now));
        var user = await older.Context.Users.AsNoTracking().SingleAsync();
        Assert.Equal(Now.AddMinutes(1), user.LastLoginAt);
        Assert.Equal(Now.AddMinutes(1), user.UpdatedAt);
        Assert.Equal(1, await older.Context.RefreshTokens.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PasswordHash_IsInUpdateSetOnlyWhenRehashIsNeeded(bool legacy)
    {
        using var database = new Database(legacy);
        var capture = new CaptureCommands();
        using var session = database.Open(capture);
        Assert.Equal(LoginStatus.Success,
            (await session.Login.LoginAsync(new LoginRequest("login@example.com", database.Password))).Status);
        var update = Assert.Single(capture.Sql, sql => sql.StartsWith("UPDATE", StringComparison.Ordinal));
        var setClause = update[..update.IndexOf("WHERE", StringComparison.Ordinal)];
        Assert.Equal(legacy, setClause.Contains("\"PasswordHash\"", StringComparison.Ordinal));
    }

    [Fact]
    public void PostgreSqlModel_HasNoPendingSchemaChanges()
    {
        using var context = new NovaCmsDbContext(new DbContextOptionsBuilder<NovaCmsDbContext>()
            .UseNpgsql("Host=unused.invalid;Database=test").Options);
        Assert.False(context.Database.HasPendingModelChanges());
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Database : IDisposable
    {
        public const string SigningKey = "fake-login-test-only-signing-key-at-least-32-bytes";
        private readonly SqliteConnection _keeper = new($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=True");
        public string Password { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        public string OriginalHash { get; }
        public ConcurrentQueue<string> Logs { get; } = new();

        public Database(bool legacy = false)
        {
            _keeper.Open();
            using var session = Open();
            session.Context.Database.EnsureCreated();
            OriginalHash = legacy
                ? new Microsoft.AspNetCore.Identity.PasswordHasher<object>(Options.Create(
                    new Microsoft.AspNetCore.Identity.PasswordHasherOptions
                    {
                        CompatibilityMode = Microsoft.AspNetCore.Identity.PasswordHasherCompatibilityMode.IdentityV2
                    })).HashPassword(new object(), Password)
                : session.Provider.GetRequiredService<IPasswordHasher>().Hash(Password);
            session.Context.Users.Add(new User(Guid.NewGuid(), "login@example.com", OriginalHash, "Test", "User", Now.AddDays(-1)));
            session.Context.SaveChanges();
        }

        public Session Open(IInterceptor? interceptor = null)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=unused.invalid;Database=test",
                ["Jwt:Issuer"] = "NovaCMS.LoginTests",
                ["Jwt:Audience"] = "NovaCMS.TestClients",
                ["Jwt:SigningKey"] = SigningKey,
                ["Jwt:AccessTokenLifetime"] = "00:15:00",
                ["Jwt:RefreshTokenLifetime"] = "7.00:00:00"
            }).Build();
            var services = new ServiceCollection();
            services.AddInfrastructure(configuration);
            services.AddSingleton<TimeProvider>(new FixedTimeProvider());
            services.AddScoped(_ =>
            {
                var options = new DbContextOptionsBuilder<NovaCmsDbContext>().UseSqlite(_keeper.ConnectionString).LogTo(Logs.Enqueue);
                if (interceptor is not null) options.AddInterceptors(interceptor);
                return new NovaCmsDbContext(options.Options);
            });
            return new Session(services.BuildServiceProvider());
        }

        public void Dispose() => _keeper.Dispose();
    }

    private sealed class Session(ServiceProvider provider) : IDisposable
    {
        public ServiceProvider Provider { get; } = provider;
        public NovaCmsDbContext Context => Provider.GetRequiredService<NovaCmsDbContext>();
        public ILoginService Login => Provider.GetRequiredService<ILoginService>();
        public void Dispose() => Provider.Dispose();
    }

    private sealed class FailCommand(string stage) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (stage == "user-update" && command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal))
                throw new InvalidOperationException("Injected user update failure.");
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (stage == "refresh-insert" && command.CommandText.StartsWith("INSERT", StringComparison.Ordinal))
                throw new InvalidOperationException("Injected refresh insert failure.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailCommit : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected commit failure.");
    }

    private sealed class BeforeCommitGate : DbTransactionInterceptor
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Ready.TrySetResult();
            await Continue.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return result;
        }
    }

    private sealed class CaptureCommands : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public List<string> Parameters { get; } = [];
        private void Capture(DbCommand command)
        {
            Sql.Add(command.CommandText);
            Parameters.AddRange(command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value?.ToString() ?? ""));
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Capture(command);
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Capture(command);
            return ValueTask.FromResult(result);
        }
    }
}
