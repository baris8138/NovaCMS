namespace NovaCMS.Application.Security;

/// <summary>
/// Commits successful login persistence before returning tokens.
/// Pending unrelated persistence changes and outer transactions are unsupported.
/// </summary>
public interface ILoginService
{
    Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
}
