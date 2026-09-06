namespace NovaCMS.Application.Security;

/// <summary>
/// Internal lifecycle operations. Tokens must never be logged or persisted by callers.
/// Operations commit before returning; pending persistence changes and outer transactions are unsupported.
/// Previously read, unchanged entities are allowed; callers should re-query a token after a lifecycle operation.
/// </summary>
public interface IRefreshTokenService
{
    Task<RefreshTokenResult> CreateAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<RefreshTokenRotationResult> RotateAsync(string token, CancellationToken cancellationToken = default);
    Task<RefreshTokenStatus> RevokeAsync(string token, CancellationToken cancellationToken = default);
}
