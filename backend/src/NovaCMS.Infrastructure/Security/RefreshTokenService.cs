using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NovaCMS.Application.Security;
using NovaCMS.Domain.Authentication;
using NovaCMS.Infrastructure.Persistence;

namespace NovaCMS.Infrastructure.Security;

internal sealed class RefreshTokenService(
    NovaCmsDbContext dbContext,
    IOptions<JwtOptions> options,
    TimeProvider timeProvider) : IRefreshTokenService
{
    private readonly JwtOptions _options = options.Value;

    public async Task<RefreshTokenResult> CreateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        EnsurePersistenceBoundary();
        var (entity, result) = Generate(userId, timeProvider.GetUtcNow());
        dbContext.RefreshTokens.Add(entity);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return result;
        }
        finally
        {
            dbContext.Entry(entity).State = EntityState.Detached;
        }
    }

    public async Task<RefreshTokenRotationResult> RotateAsync(
        string token, CancellationToken cancellationToken = default)
    {
        EnsurePersistenceBoundary();
        var existing = await FindAsync(token, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var status = GetStatus(existing, now);
        if (status != RefreshTokenStatus.Success)
        {
            return RefreshTokenRotationResult.Failed(status);
        }

        var (replacement, result) = Generate(existing!.UserId, now);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.RefreshTokens.Add(replacement);
        try
        {
            // Insert first to satisfy the replacement FK. Both writes belong to this transaction.
            await dbContext.SaveChangesAsync(cancellationToken);
            var updated = await dbContext.RefreshTokens
                .Where(candidate => candidate.Id == existing.Id &&
                    candidate.RevokedAt == null && candidate.ReplacedByTokenId == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(candidate => candidate.RevokedAt, (DateTimeOffset?)now)
                    .SetProperty(candidate => candidate.ReplacedByTokenId, (Guid?)replacement.Id), cancellationToken);

            if (updated != 1)
            {
                // A concurrent rotation/revocation won. Disposing rolls back the unused replacement.
                return RefreshTokenRotationResult.Failed(RefreshTokenStatus.Revoked);
            }

            await transaction.CommitAsync(cancellationToken);
            return RefreshTokenRotationResult.Succeeded(result);
        }
        finally
        {
            dbContext.Entry(replacement).State = EntityState.Detached;
        }
    }

    public async Task<RefreshTokenStatus> RevokeAsync(string token, CancellationToken cancellationToken = default)
    {
        EnsurePersistenceBoundary();
        var existing = await FindAsync(token, cancellationToken);
        if (existing is null)
        {
            return RefreshTokenStatus.NotFound;
        }

        // Expired tokens may also be revoked. Repeated revocation preserves the original timestamp.
        var now = timeProvider.GetUtcNow();
        var updated = await dbContext.RefreshTokens
            .Where(candidate => candidate.Id == existing.Id &&
                candidate.RevokedAt == null && candidate.ReplacedByTokenId == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.RevokedAt, (DateTimeOffset?)now), cancellationToken);
        return updated == 1 ? RefreshTokenStatus.Success : RefreshTokenStatus.Revoked;
    }

    private Task<RefreshToken?> FindAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult<RefreshToken?>(null);
        }

        var hash = Hash(token);
        // ExecuteUpdate bypasses tracking. A caller may already have loaded this token;
        // detach that snapshot so later tracked queries cannot return its stale active state.
        foreach (var entry in dbContext.ChangeTracker.Entries<RefreshToken>()
            .Where(entry => entry.Entity.TokenHash == hash).ToList())
        {
            entry.State = EntityState.Detached;
        }

        return dbContext.RefreshTokens.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);
    }

    private (RefreshToken Entity, RefreshTokenResult Result) Generate(Guid userId, DateTimeOffset now)
    {
        // 32 random bytes = 256 bits of entropy. Encoding is transport formatting, not encryption.
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expiresAt = now.Add(_options.RefreshTokenLifetime);
        return (new RefreshToken(Guid.NewGuid(), userId, Hash(token), expiresAt, now),
            new RefreshTokenResult(token, expiresAt));
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static RefreshTokenStatus GetStatus(RefreshToken? token, DateTimeOffset now) => token switch
    {
        null => RefreshTokenStatus.NotFound,
        { RevokedAt: not null } or { ReplacedByTokenId: not null } => RefreshTokenStatus.Revoked,
        _ when token.ExpiresAt <= now => RefreshTokenStatus.Expired,
        _ => RefreshTokenStatus.Success
    };

    private void EnsurePersistenceBoundary()
    {
        // These operations commit before returning a secret. Do not accidentally save caller changes
        // or return a token whose persistence depends on an outer transaction committing later.
        if (dbContext.ChangeTracker.HasChanges() || dbContext.Database.CurrentTransaction is not null ||
            System.Transactions.Transaction.Current is not null)
        {
            throw new InvalidOperationException("Refresh token operations require a clean context and no outer transaction.");
        }
    }
}
