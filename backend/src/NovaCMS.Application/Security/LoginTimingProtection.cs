namespace NovaCMS.Application.Security;

/// <summary>One dummy hash per singleton instance, using the configured password hasher.</summary>
public sealed class LoginTimingProtection
{
    private readonly IPasswordHasher _hasher;
    private readonly string _dummyHash;

    public LoginTimingProtection(IPasswordHasher hasher)
    {
        _hasher = hasher;
        _dummyHash = hasher.Hash(Guid.NewGuid().ToString("N"));
    }

    public void VerifyDummy(string password) => _hasher.Verify(password, _dummyHash);
}
