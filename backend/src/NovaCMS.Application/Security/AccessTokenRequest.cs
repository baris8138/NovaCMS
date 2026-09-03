namespace NovaCMS.Application.Security;

public sealed record AccessTokenRequest(Guid UserId, string Email);
