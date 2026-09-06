namespace NovaCMS.Application.Security;

public enum LoginStatus
{
    Success,
    InvalidCredentials
}

/// <summary>Contains delivery secrets only on success. Never log or persist this result.</summary>
public sealed class LoginResult
{
    private LoginResult(LoginStatus status, AccessTokenResult? accessToken, RefreshTokenResult? refreshToken)
    {
        Status = status;
        AccessToken = accessToken?.Token;
        AccessTokenExpiresAt = accessToken?.ExpiresAt;
        RefreshToken = refreshToken?.Token;
        RefreshTokenExpiresAt = refreshToken?.ExpiresAt;
    }

    public LoginStatus Status { get; }
    public string? AccessToken { get; }
    public DateTimeOffset? AccessTokenExpiresAt { get; }
    public string? RefreshToken { get; }
    public DateTimeOffset? RefreshTokenExpiresAt { get; }

    public static LoginResult InvalidCredentials() => new(LoginStatus.InvalidCredentials, null, null);

    public static LoginResult Succeeded(AccessTokenResult accessToken, RefreshTokenResult refreshToken)
    {
        ArgumentNullException.ThrowIfNull(accessToken);
        ArgumentNullException.ThrowIfNull(refreshToken);
        return new(LoginStatus.Success, accessToken, refreshToken);
    }

    public override string ToString() => $"{nameof(LoginResult)}: {Status}";
}
