namespace NovaCMS.Application.Security;

public interface IAccessTokenGenerator
{
    AccessTokenResult Generate(AccessTokenRequest request);
}
