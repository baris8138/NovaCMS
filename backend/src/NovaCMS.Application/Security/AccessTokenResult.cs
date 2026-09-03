namespace NovaCMS.Application.Security;

public sealed record AccessTokenResult(string Token, DateTimeOffset ExpiresAt);
