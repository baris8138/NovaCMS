namespace NovaCMS.Application.Security;

/// <summary>Transient credentials. Do not log or persist this request.</summary>
public sealed class LoginRequest(string email, string password)
{
    public string Email { get; } = email;
    public string Password { get; } = password;

    public override string ToString() => nameof(LoginRequest);
}
