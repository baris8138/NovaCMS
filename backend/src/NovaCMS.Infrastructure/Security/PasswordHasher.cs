using NovaCMS.Application.Security;
using IdentityPasswordHasher = Microsoft.AspNetCore.Identity.PasswordHasher<object>;
using IdentityPasswordVerificationResult = Microsoft.AspNetCore.Identity.PasswordVerificationResult;

namespace NovaCMS.Infrastructure.Security;

internal sealed class PasswordHasher : IPasswordHasher
{
    private static readonly object User = new();
    private readonly IdentityPasswordHasher _hasher = new();

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        return _hasher.HashPassword(User, password);
    }

    public PasswordVerificationResult Verify(string password, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(passwordHash))
        {
            return PasswordVerificationResult.Failed;
        }

        try
        {
            return _hasher.VerifyHashedPassword(User, passwordHash, password) switch
            {
                IdentityPasswordVerificationResult.Success => PasswordVerificationResult.Success,
                IdentityPasswordVerificationResult.SuccessRehashNeeded =>
                    PasswordVerificationResult.SuccessRehashNeeded,
                _ => PasswordVerificationResult.Failed
            };
        }
        catch (FormatException)
        {
            return PasswordVerificationResult.Failed;
        }
        catch (ArgumentException)
        {
            return PasswordVerificationResult.Failed;
        }
    }
}
