using NovaCMS.Domain.Authentication;

namespace NovaCMS.Application.Security;

public sealed class LoginService(
    IAuthenticationUserStore users,
    IPasswordHasher passwordHasher,
    IAccessTokenGenerator accessTokens,
    LoginTimingProtection timingProtection,
    TimeProvider timeProvider) : ILoginService
{
    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return LoginResult.InvalidCredentials();

        var user = await users.FindByNormalizedEmailAsync(User.NormalizeEmail(request.Email), cancellationToken);
        if (user is null || !user.IsActive)
        {
            // Reduce the obvious missing/inactive-user fast path; this is not a constant-time guarantee.
            timingProtection.VerifyDummy(request.Password);
            return LoginResult.InvalidCredentials();
        }

        var verification = passwordHasher.Verify(request.Password, user.PasswordHash);
        if (verification is not (PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded))
            return LoginResult.InvalidCredentials();

        var newHash = verification == PasswordVerificationResult.SuccessRehashNeeded
            ? passwordHasher.Hash(request.Password)
            : null;
        var accessToken = accessTokens.Generate(new AccessTokenRequest(user.Id, user.Email));
        var refreshToken = await users.CompleteLoginAsync(user, newHash, timeProvider.GetUtcNow(), cancellationToken);
        return refreshToken is null
            ? LoginResult.InvalidCredentials()
            : LoginResult.Succeeded(accessToken, refreshToken);
    }
}
