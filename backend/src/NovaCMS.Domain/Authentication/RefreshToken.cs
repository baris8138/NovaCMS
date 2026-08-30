namespace NovaCMS.Domain.Authentication;

public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    public RefreshToken(
        Guid id,
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAt,
        DateTimeOffset createdAt)
    {
        Id = id == Guid.Empty ? throw new ArgumentException("Refresh token ID cannot be empty.", nameof(id)) : id;
        UserId = userId == Guid.Empty ? throw new ArgumentException("User ID cannot be empty.", nameof(userId)) : userId;
        TokenHash = string.IsNullOrWhiteSpace(tokenHash)
            ? throw new ArgumentException("Token hash cannot be empty or whitespace.", nameof(tokenHash))
            : tokenHash.Trim();
        ExpiresAt = expiresAt > createdAt
            ? expiresAt
            : throw new ArgumentOutOfRangeException(nameof(expiresAt), "Expiration must be later than creation.");
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }
    public User User { get; private set; } = null!;
    public RefreshToken? ReplacedByToken { get; private set; }
}
