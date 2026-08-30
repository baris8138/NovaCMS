namespace NovaCMS.Domain.Authentication;

public sealed class User
{
    private readonly List<RefreshToken> _refreshTokens = [];

    private User()
    {
    }

    public User(
        Guid id,
        string email,
        string passwordHash,
        string firstName,
        string lastName,
        DateTimeOffset createdAt)
    {
        Id = id == Guid.Empty ? throw new ArgumentException("User ID cannot be empty.", nameof(id)) : id;
        SetEmail(email);
        PasswordHash = RequireValue(passwordHash, nameof(passwordHash));
        FirstName = RequireValue(firstName, nameof(firstName));
        LastName = RequireValue(lastName, nameof(lastName));
        IsActive = true;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string NormalizedEmail { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens;

    private void SetEmail(string email)
    {
        Email = RequireValue(email, nameof(email));
        NormalizedEmail = Email.ToUpperInvariant();
    }

    private static string RequireValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty or whitespace.", parameterName);
        }

        return value.Trim();
    }
}
