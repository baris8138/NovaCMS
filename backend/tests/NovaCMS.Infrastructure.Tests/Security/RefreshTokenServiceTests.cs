using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NovaCMS.Application.Security;
using NovaCMS.Domain.Authentication;
using NovaCMS.Infrastructure.Persistence;
using Xunit;

namespace NovaCMS.Infrastructure.Tests.Security;

public sealed class RefreshTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_GeneratesUnique256BitBase64UrlTokensAndDeterministicHashes()
    {
        using var database = new Database();
        using var session = database.Open();
        var first = await session.Service.CreateAsync(database.UserId);
        var second = await session.Service.CreateAsync(database.UserId);
        Assert.NotEqual(first.Token, second.Token);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", first.Token);
        Assert.Equal(32, Convert.FromBase64String(first.Token.Replace('-', '+').Replace('_', '/') + "=").Length);
        var rows = await session.Context.RefreshTokens.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.TokenHash == Hash(first.Token));
        Assert.Contains(rows, row => row.TokenHash == Hash(second.Token));
        Assert.Equal(2, rows.Select(row => row.TokenHash).Distinct().Count());
        Assert.All(rows, row =>
        {
            Assert.Matches("^[A-F0-9]{64}$", row.TokenHash);
            Assert.NotEqual(first.Token, row.TokenHash);
            Assert.Equal(Now, row.CreatedAt);
            Assert.Equal(Now.AddDays(7), row.ExpiresAt);
            Assert.Null(row.RevokedAt);
        });
        Assert.Equal(Now.AddDays(7), first.ExpiresAt);
        // Repeated lookup must compute exactly the same digest as creation.
        Assert.Equal(RefreshTokenStatus.Success, await session.Service.RevokeAsync(first.Token));
        Assert.Equal(RefreshTokenStatus.Revoked, await session.Service.RevokeAsync(first.Token));
    }

    [Fact]
    public async Task Create_UsesConfiguredLifetimeAndClock()
    {
        using var database = new Database();
        using var session = database.Open(lifetime: "2.03:00:00");
        var result = await session.Service.CreateAsync(database.UserId);
        Assert.Equal(Now.AddDays(2).AddHours(3), result.ExpiresAt);
        Assert.Equal(result.ExpiresAt, (await session.Context.RefreshTokens.SingleAsync()).ExpiresAt);
    }

    [Fact]
    public async Task Rotate_RevokesLinksAndPersistsOnlyReplacementHash()
    {
        using var database = new Database();
        using var session = database.Open();
        var original = await session.Service.CreateAsync(database.UserId);
        database.Clock.Now = Now.AddHours(1);
        var result = await session.Service.RotateAsync(original.Token);
        Assert.Equal(RefreshTokenStatus.Success, result.Status);
        var delivered = Assert.IsType<RefreshTokenResult>(result.RefreshToken);
        Assert.NotEqual(original.Token, delivered.Token);
        var rows = await session.Context.RefreshTokens.AsNoTracking().ToListAsync();
        var old = rows.Single(row => row.TokenHash == Hash(original.Token));
        var replacement = rows.Single(row => row.Id == old.ReplacedByTokenId);
        Assert.Equal(database.Clock.Now, old.RevokedAt);
        Assert.Equal(Hash(delivered.Token), replacement.TokenHash);
        Assert.Equal(database.UserId, replacement.UserId);
        Assert.Equal(database.Clock.Now, replacement.CreatedAt);
        Assert.Equal(database.Clock.Now.AddDays(7), delivered.ExpiresAt);
        Assert.Equal(delivered.ExpiresAt, replacement.ExpiresAt);
        Assert.Null(replacement.RevokedAt);
        Assert.Null(replacement.ReplacedByTokenId);
        Assert.Equal(RefreshTokenStatus.Success, (await session.Service.RotateAsync(delivered.Token)).Status);
    }

    [Fact]
    public async Task Rotate_RejectsReuseWithoutCreatingAnotherRow()
    {
        using var database = new Database();
        using var session = database.Open();
        var original = await session.Service.CreateAsync(database.UserId);
        await session.Service.RotateAsync(original.Token);
        var replay = await session.Service.RotateAsync(original.Token);
        Assert.Equal(RefreshTokenStatus.Revoked, replay.Status);
        Assert.Null(replay.RefreshToken);
        Assert.Equal(2, await session.Context.RefreshTokens.CountAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Rotate_RejectsExpirationAtOrBeforeNow(int secondsAfterExpiry)
    {
        using var database = new Database();
        using var session = database.Open();
        var original = await session.Service.CreateAsync(database.UserId);
        database.Clock.Now = original.ExpiresAt.AddSeconds(secondsAfterExpiry);
        var result = await session.Service.RotateAsync(original.Token);
        Assert.Equal(RefreshTokenStatus.Expired, result.Status);
        Assert.Null(result.RefreshToken);
        Assert.Null((await session.Context.RefreshTokens.SingleAsync()).RevokedAt);
        Assert.Equal(1, await session.Context.RefreshTokens.CountAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("clearly-fake-unknown-test-token")]
    public async Task UnknownOrEmptyToken_ReturnsSafeFailures(string? token)
    {
        using var database = new Database();
        using var session = database.Open();
        var rotation = await session.Service.RotateAsync(token!);
        Assert.Equal(RefreshTokenStatus.NotFound, rotation.Status);
        Assert.Null(rotation.RefreshToken);
        Assert.Equal(RefreshTokenStatus.NotFound, await session.Service.RevokeAsync(token!));
        Assert.Empty(await session.Context.RefreshTokens.ToListAsync());
    }

    [Fact]
    public async Task Revoke_PreservesFirstTimestampAndPreventsRotation()
    {
        using var database = new Database();
        using var session = database.Open();
        var original = await session.Service.CreateAsync(database.UserId);
        Assert.Equal(RefreshTokenStatus.Success, await session.Service.RevokeAsync(original.Token));
        database.Clock.Now = Now.AddHours(1);
        Assert.Equal(RefreshTokenStatus.Revoked, await session.Service.RevokeAsync(original.Token));
        Assert.Equal(RefreshTokenStatus.Revoked, (await session.Service.RotateAsync(original.Token)).Status);
        var row = await session.Context.RefreshTokens.SingleAsync();
        Assert.Equal(Now, row.RevokedAt);
        Assert.Null(row.ReplacedByTokenId);
    }

    [Fact]
    public async Task Revoke_CanRevokeExpiredToken()
    {
        using var database = new Database();
        using var session = database.Open();
        var original = await session.Service.CreateAsync(database.UserId);
        database.Clock.Now = original.ExpiresAt;
        Assert.Equal(RefreshTokenStatus.Success, await session.Service.RevokeAsync(original.Token));
    }

    [Fact]
    public async Task Rotate_RejectsReplacementLinkEvenWithoutRevocationTimestamp()
    {
        using var database = new Database();
        using var session = database.Open();
        var original = await session.Service.CreateAsync(database.UserId);
        await session.Service.RotateAsync(original.Token);
        await session.Context.RefreshTokens.Where(row => row.TokenHash == Hash(original.Token))
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RevokedAt, (DateTimeOffset?)null));
        Assert.Equal(RefreshTokenStatus.Revoked, (await session.Service.RotateAsync(original.Token)).Status);
        Assert.Equal(RefreshTokenStatus.Revoked, await session.Service.RevokeAsync(original.Token));
        Assert.Equal(2, await session.Context.RefreshTokens.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rotate_WhenUpdateOrCommitFails_RollsBackAndLeavesOriginalActive(bool failCommit)
    {
        using var database = new Database();
        string token;
        using (var setup = database.Open())
            token = (await setup.Service.CreateAsync(database.UserId)).Token;
        using (var failing = database.Open(failCommit ? new FailCommit() : new FailUpdate()))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => failing.Service.RotateAsync(token));
            Assert.False(failing.Context.ChangeTracker.HasChanges());
            Assert.Empty(failing.Context.ChangeTracker.Entries<RefreshToken>());
        }
        using var verify = database.Open();
        var row = await verify.Context.RefreshTokens.AsNoTracking().SingleAsync();
        Assert.Null(row.RevokedAt);
        Assert.Null(row.ReplacedByTokenId);
        Assert.Equal(RefreshTokenStatus.Success, (await verify.Service.RotateAsync(token)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentStaleRead_OnlyOneRotationOrRevocationWins(bool revokeWins)
    {
        using var database = new Database();
        using var winner = database.Open();
        var original = await winner.Service.CreateAsync(database.UserId);
        var gate = new BeforeTransactionGate();
        using var loser = database.Open(gate);
        // The losing request reads an active row, then pauses before acquiring its transaction.
        var pending = loser.Service.RotateAsync(original.Token);
        await gate.ReadCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        RefreshTokenResult? winningToken = null;
        try
        {
            if (revokeWins)
                Assert.Equal(RefreshTokenStatus.Success, await winner.Service.RevokeAsync(original.Token));
            else
            {
                var rotation = await winner.Service.RotateAsync(original.Token);
                Assert.Equal(RefreshTokenStatus.Success, rotation.Status);
                winningToken = Assert.IsType<RefreshTokenResult>(rotation.RefreshToken);
            }
        }
        finally
        {
            gate.Continue.TrySetResult();
        }
        var rejected = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(RefreshTokenStatus.Revoked, rejected.Status);
        Assert.Null(rejected.RefreshToken);
        Assert.Equal(revokeWins ? 1 : 2, await winner.Context.RefreshTokens.CountAsync());
        var rows = await winner.Context.RefreshTokens.AsNoTracking().ToListAsync();
        var parent = rows.Single(row => row.TokenHash == Hash(original.Token));
        Assert.NotNull(parent.RevokedAt);
        if (winningToken is not null)
        {
            var child = rows.Single(row => row.Id == parent.ReplacedByTokenId);
            Assert.Equal(Hash(winningToken.Token), child.TokenHash);
            Assert.Null(child.RevokedAt);
            Assert.Null(child.ReplacedByTokenId);
        }
        else
            Assert.Null(parent.ReplacedByTokenId);
        Assert.Empty(loser.Context.ChangeTracker.Entries<RefreshToken>());
    }

    [Fact]
    public async Task Security_ModelSqlParametersAndLogsExcludePlaintext()
    {
        using var database = new Database();
        var capture = new CommandCapture();
        using var session = database.Open(capture);
        var original = await session.Service.CreateAsync(database.UserId);
        var rotated = (await session.Service.RotateAsync(original.Token)).RefreshToken!;
        await session.Service.RevokeAsync(rotated.Token);
        var model = session.Context.Model.FindEntityType(typeof(RefreshToken))!;
        Assert.Equal(new[] { "TokenHash" }, model.GetProperties()
            .Where(property => property.ClrType == typeof(string)).Select(property => property.Name));
        Assert.DoesNotContain(typeof(RefreshToken).GetProperties(), property =>
            property.Name is "Token" or "RawToken" or "RefreshTokenValue");
        Assert.Contains(model.GetIndexes(), index => index.IsUnique && index.Properties.Single().Name == "TokenHash");
        var commands = string.Join('\n', capture.Commands);
        Assert.Contains(Hash(original.Token), commands);
        Assert.Contains(Hash(rotated.Token), commands);
        Assert.DoesNotContain(original.Token, commands);
        Assert.DoesNotContain(rotated.Token, commands);
        var updates = capture.Commands.Where(command => command.StartsWith("UPDATE", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, updates.Count);
        Assert.All(updates, sql =>
        {
            Assert.Contains("WHERE \"r\".\"Id\" = @", sql);
            Assert.Contains("AND \"r\".\"RevokedAt\" IS NULL AND \"r\".\"ReplacedByTokenId\" IS NULL", sql);
        });
        var logs = string.Join('\n', database.Logs);
        Assert.DoesNotContain(original.Token, logs);
        Assert.DoesNotContain(rotated.Token, logs);
        Assert.DoesNotContain(Hash(original.Token), logs);
        Assert.DoesNotContain(Hash(rotated.Token), logs);
        Assert.DoesNotContain(original.Token, original.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00:00:00")]
    [InlineData("-00:00:01")]
    public void InvalidLifetime_FailsStartupValidation(string? lifetime)
    {
        using var database = new Database();
        using var session = database.Open(lifetime: lifetime);
        Assert.Throws<OptionsValidationException>(() => session.Provider.GetRequiredService<IStartupValidator>().Validate());
    }

    [Fact]
    public void ValidConfiguration_ValidatesAndRegistersScopedService()
    {
        using var database = new Database();
        using var session = database.Open();
        session.Provider.GetRequiredService<IStartupValidator>().Validate();
        using var first = session.Provider.CreateScope();
        using var second = session.Provider.CreateScope();
        Assert.Same(first.ServiceProvider.GetRequiredService<IRefreshTokenService>(),
            first.ServiceProvider.GetRequiredService<IRefreshTokenService>());
        Assert.NotSame(first.ServiceProvider.GetRequiredService<IRefreshTokenService>(),
            second.ServiceProvider.GetRequiredService<IRefreshTokenService>());
    }

    [Fact]
    public async Task Create_InvalidUser_DoesNotPersistOrLeavePendingToken()
    {
        using var database = new Database();
        using var session = database.Open();
        await Assert.ThrowsAsync<ArgumentException>(() => session.Service.CreateAsync(Guid.Empty));
        await Assert.ThrowsAsync<DbUpdateException>(() => session.Service.CreateAsync(Guid.NewGuid()));
        Assert.False(session.Context.ChangeTracker.HasChanges());
        Assert.Empty(await session.Context.RefreshTokens.ToListAsync());
    }

    [Fact]
    public async Task Operations_RejectUncommittedCallerChangesAndOuterTransactions()
    {
        using var database = new Database();
        using var session = database.Open();
        var original = await session.Service.CreateAsync(database.UserId);
        session.Context.Users.Add(new User(Guid.NewGuid(), "pending@example.com", "fake-test-hash", "Test", "User", Now));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Service.CreateAsync(database.UserId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Service.RotateAsync(original.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Service.RevokeAsync(original.Token));
        session.Context.ChangeTracker.Clear();
        await using var transaction = await session.Context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Service.CreateAsync(database.UserId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Service.RotateAsync(original.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Service.RevokeAsync(original.Token));
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lifecycle_DetachesPreviouslyTrackedTokenAndPreservesUnrelatedTracking(bool revoke)
    {
        using var database = new Database();
        using var session = database.Open();
        var user = await session.Context.Users.SingleAsync();
        var original = await session.Service.CreateAsync(database.UserId);
        var tracked = await session.Context.RefreshTokens.SingleAsync();

        if (revoke)
            Assert.Equal(RefreshTokenStatus.Success, await session.Service.RevokeAsync(original.Token));
        else
            Assert.Equal(RefreshTokenStatus.Success, (await session.Service.RotateAsync(original.Token)).Status);

        Assert.Equal(EntityState.Detached, session.Context.Entry(tracked).State);
        Assert.Equal(EntityState.Unchanged, session.Context.Entry(user).State);
        var refreshed = await session.Context.RefreshTokens.FindAsync(tracked.Id);
        Assert.NotSame(tracked, refreshed);
        Assert.Equal(Now, refreshed!.RevokedAt);
        Assert.Equal(!revoke, refreshed.ReplacedByTokenId.HasValue);
        Assert.Equal(0, await session.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task RotationChain_PreservesBothLinksRejectsAncestorsAndRestrictsChildDeletion()
    {
        using var database = new Database();
        using var session = database.Open();
        var a = await session.Service.CreateAsync(database.UserId);
        var b = (await session.Service.RotateAsync(a.Token)).RefreshToken!;
        var c = (await session.Service.RotateAsync(b.Token)).RefreshToken!;
        var rows = await session.Context.RefreshTokens.AsNoTracking().ToListAsync();
        var parent = rows.Single(row => row.TokenHash == Hash(a.Token));
        var middle = rows.Single(row => row.TokenHash == Hash(b.Token));
        var child = rows.Single(row => row.TokenHash == Hash(c.Token));
        Assert.Equal(middle.Id, parent.ReplacedByTokenId);
        Assert.Equal(child.Id, middle.ReplacedByTokenId);
        Assert.NotNull(parent.RevokedAt);
        Assert.NotNull(middle.RevokedAt);
        Assert.Null(child.RevokedAt);
        Assert.Null(child.ReplacedByTokenId);
        Assert.True(child.ExpiresAt > database.Clock.Now);
        Assert.Equal(RefreshTokenStatus.Revoked, (await session.Service.RotateAsync(a.Token)).Status);
        Assert.Equal(RefreshTokenStatus.Revoked, (await session.Service.RotateAsync(b.Token)).Status);
        var foreignKey = session.Context.Model.FindEntityType(typeof(RefreshToken))!.GetForeignKeys()
            .Single(key => key.Properties.Any(property => property.Name == nameof(RefreshToken.ReplacedByTokenId)));
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        await Assert.ThrowsAsync<SqliteException>(() => session.Context.RefreshTokens
            .Where(row => row.Id == child.Id).ExecuteDeleteAsync());
        Assert.Equal(3, await session.Context.RefreshTokens.CountAsync());
    }

    [Fact]
    public async Task DuplicateHash_IsRejectedWithoutLeakingValuesInExceptionOrLogs()
    {
        using var database = new Database();
        using var session = database.Open();
        var original = await session.Service.CreateAsync(database.UserId);
        var hash = Hash(original.Token);
        session.Context.RefreshTokens.Add(new RefreshToken(Guid.NewGuid(), database.UserId, hash, Now.AddDays(7), Now));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => session.Context.SaveChangesAsync());
        Assert.DoesNotContain(original.Token, error.ToString());
        Assert.DoesNotContain(hash, error.ToString());
        Assert.DoesNotContain(original.Token, string.Join('\n', database.Logs));
        Assert.DoesNotContain(hash, string.Join('\n', database.Logs));
        session.Context.ChangeTracker.Clear();
        Assert.Equal(1, await session.Context.RefreshTokens.CountAsync());
    }

    [Fact]
    public async Task Operations_RejectAmbientTransactionBeforeDatabaseAccess()
    {
        using var database = new Database();
        using var session = database.Open();
        using var transaction = new System.Transactions.TransactionScope(
            System.Transactions.TransactionScopeAsyncFlowOption.Enabled);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Service.CreateAsync(database.UserId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Service.RotateAsync("fake-test-token"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Service.RevokeAsync("fake-test-token"));
    }

    [Fact]
    public async Task PostgreSql_TranslatesConditionalUpdateWithoutOpeningDatabase()
    {
        // Translation-only check of the production predicate and setters, not a PostgreSQL execution test.
        var capture = new CommandCapture(suppressExecution: true);
        var options = new DbContextOptionsBuilder<NovaCmsDbContext>()
            .UseNpgsql("Host=unused.invalid;Database=unused")
            .AddInterceptors(new SuppressConnectionOpen(), capture).Options;
        using var context = new NovaCmsDbContext(options);
        var id = Guid.NewGuid();
        var replacementId = Guid.NewGuid();
        await context.RefreshTokens.Where(candidate => candidate.Id == id &&
                candidate.RevokedAt == null && candidate.ReplacedByTokenId == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.RevokedAt, (DateTimeOffset?)Now)
                .SetProperty(candidate => candidate.ReplacedByTokenId, (Guid?)replacementId));
        var sql = Assert.Single(capture.Commands);
        Assert.Contains("UPDATE \"RefreshTokens\" AS r", sql);
        Assert.Contains("WHERE r.\"Id\" = @", sql);
        Assert.Contains("AND r.\"RevokedAt\" IS NULL AND r.\"ReplacedByTokenId\" IS NULL", sql);
        Assert.DoesNotContain("SELECT", sql);
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = RefreshTokenServiceTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Database : IDisposable
    {
        private readonly SqliteConnection _keeper = new($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=True");
        public Guid UserId { get; } = Guid.NewGuid();
        public TestTimeProvider Clock { get; } = new();
        public ConcurrentQueue<string> Logs { get; } = new();

        public Database()
        {
            _keeper.Open();
            using var session = Open();
            session.Context.Database.EnsureCreated();
            session.Context.Users.Add(new User(UserId, "test@example.com", "fake-test-password-hash", "Test", "User", Now));
            session.Context.SaveChanges();
        }

        public Session Open(IInterceptor? interceptor = null, string? lifetime = "7.00:00:00")
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=test;Database=test",
                ["Jwt:Issuer"] = "NovaCMS.Tests",
                ["Jwt:Audience"] = "NovaCMS.TestClients",
                ["Jwt:SigningKey"] = "fake-test-only-signing-key-at-least-32-bytes",
                ["Jwt:AccessTokenLifetime"] = "00:15:00",
                ["Jwt:RefreshTokenLifetime"] = lifetime
            }).Build();
            var services = new ServiceCollection();
            services.AddInfrastructure(configuration);
            services.AddSingleton<TimeProvider>(Clock);
            services.AddScoped(_ =>
            {
                var builder = new DbContextOptionsBuilder<NovaCmsDbContext>()
                    .UseSqlite(_keeper.ConnectionString).LogTo(Logs.Enqueue);
                if (interceptor is not null) builder.AddInterceptors(interceptor);
                return new NovaCmsDbContext(builder.Options);
            });
            return new Session(services.BuildServiceProvider());
        }

        public void Dispose() => _keeper.Dispose();
    }

    private sealed class Session(ServiceProvider provider) : IDisposable
    {
        public ServiceProvider Provider { get; } = provider;
        public NovaCmsDbContext Context => Provider.GetRequiredService<NovaCmsDbContext>();
        public IRefreshTokenService Service => Provider.GetRequiredService<IRefreshTokenService>();
        public void Dispose() => Provider.Dispose();
    }

    private sealed class FailUpdate : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal))
                throw new InvalidOperationException("Injected test update failure.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class BeforeTransactionGate : DbTransactionInterceptor
    {
        public TaskCompletionSource ReadCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            ReadCompleted.TrySetResult();
            await Continue.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return result;
        }
    }

    private sealed class FailCommit : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected test commit failure.");
    }

    private sealed class SuppressConnectionOpen : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult.Suppress());
    }

    private sealed class CommandCapture(bool suppressExecution = false) : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        private void Capture(DbCommand command) => Commands.Add(command.CommandText + "\n" +
            string.Join('|', command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value)));

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Capture(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Capture(command);
            return ValueTask.FromResult(suppressExecution ? InterceptionResult<int>.SuppressWithResult(0) : result);
        }
    }
}
