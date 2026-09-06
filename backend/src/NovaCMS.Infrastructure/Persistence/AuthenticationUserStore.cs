using Microsoft.EntityFrameworkCore;
using NovaCMS.Application.Security;
using NovaCMS.Domain.Authentication;
using NovaCMS.Infrastructure.Security;

namespace NovaCMS.Infrastructure.Persistence;

internal sealed class AuthenticationUserStore(
    NovaCmsDbContext dbContext,
    RefreshTokenFactory tokenFactory) : IAuthenticationUserStore
{
    public Task<User?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        dbContext.Users.AsNoTracking().SingleOrDefaultAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken);

    public async Task<RefreshTokenResult?> CompleteLoginAsync(User verifiedUser, string? replacementPasswordHash,
        DateTimeOffset loginAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verifiedUser);
        if (dbContext.ChangeTracker.HasChanges() || dbContext.Database.CurrentTransaction is not null ||
            System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Login persistence requires no pending changes or outer transaction.");

        // ExecuteUpdate bypasses tracking, so remove only an existing snapshot of this user.
        foreach (var entry in dbContext.ChangeTracker.Entries<User>().Where(entry => entry.Entity.Id == verifiedUser.Id).ToList())
            entry.State = EntityState.Detached;

        var (refreshToken, result) = tokenFactory.Generate(verifiedUser.Id, loginAt);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Recheck credentials and activation in SQL, preventing stale verification from overwriting
            // a changed password or issuing a token after account deactivation.
            var updated = await dbContext.Users
                .Where(user => user.Id == verifiedUser.Id && user.IsActive &&
                    user.PasswordHash == verifiedUser.PasswordHash && user.Email == verifiedUser.Email &&
                    user.LastLoginAt == verifiedUser.LastLoginAt && user.UpdatedAt == verifiedUser.UpdatedAt)
                .ExecuteUpdateAsync(setters =>
                {
                    setters.SetProperty(user => user.LastLoginAt, (DateTimeOffset?)loginAt)
                        .SetProperty(user => user.UpdatedAt, loginAt);
                    if (replacementPasswordHash is not null)
                        setters.SetProperty(user => user.PasswordHash, replacementPasswordHash);
                }, cancellationToken);
            if (updated != 1)
                return null;

            dbContext.RefreshTokens.Add(refreshToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        finally
        {
            dbContext.Entry(refreshToken).State = EntityState.Detached;
        }
    }
}
