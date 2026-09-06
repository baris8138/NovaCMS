namespace NovaCMS.Application.Security;

/// <summary>Short-lived delivery result; contains the secret only for the caller.</summary>
public sealed class RefreshTokenResult(string token, DateTimeOffset expiresAt)
{
    public string Token { get; } = token;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;

    public override string ToString() => nameof(RefreshTokenResult);
}

// Internal outcomes; an HTTP boundary must choose its own disclosure policy.
public enum RefreshTokenStatus
{
    Success,
    NotFound,
    Expired,
    Revoked
}

public sealed class RefreshTokenRotationResult
{
    private RefreshTokenRotationResult(RefreshTokenStatus status, RefreshTokenResult? refreshToken)
    {
        Status = status;
        RefreshToken = refreshToken;
    }

    public RefreshTokenStatus Status { get; }
    public RefreshTokenResult? RefreshToken { get; }

    public static RefreshTokenRotationResult Succeeded(RefreshTokenResult refreshToken) =>
        new(RefreshTokenStatus.Success, refreshToken ?? throw new ArgumentNullException(nameof(refreshToken)));

    public static RefreshTokenRotationResult Failed(RefreshTokenStatus status) =>
        status == RefreshTokenStatus.Success
            ? throw new ArgumentException("A failure cannot have success status.", nameof(status))
            : new(status, null);
}
