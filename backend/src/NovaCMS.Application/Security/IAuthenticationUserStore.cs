using NovaCMS.Domain.Authentication;

namespace NovaCMS.Application.Security;

/// <summary>Login-specific persistence, not a general user repository.</summary>
public interface IAuthenticationUserStore
{
    Task<User?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically commits the login timestamp, optional password rehash and a new refresh token.
    /// Returns null if activation, credentials or login/update timestamps changed since verification.
    /// Requires no pending persistence changes or outer transaction; returns only after commit.
    /// </summary>
    Task<RefreshTokenResult?> CompleteLoginAsync(User verifiedUser, string? replacementPasswordHash,
        DateTimeOffset loginAt, CancellationToken cancellationToken = default);
}
