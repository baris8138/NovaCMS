using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NovaCMS.Application.Security;
using NovaCMS.Infrastructure;
using Xunit;

namespace NovaCMS.Infrastructure.Tests.Security;

public sealed class JwtAccessTokenGeneratorTests
{
    private const string Issuer = "NovaCMS.Tests";
    private const string Audience = "NovaCMS.TestClients";
    private const string SigningKey = "test-only-signing-key-with-32-bytes-minimum";
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 10, 30, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.Parse("781c30f8-3365-464f-80f7-114496fa520f");

    [Fact]
    public void Generate_WithValidInput_ReturnsSignedTokenWithExpectedMetadataAndClaims()
    {
        var generator = CreateGenerator();

        var result = generator.Generate(new AccessTokenRequest(UserId, "user@example.com"));
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);

        Assert.NotEmpty(result.Token);
        Assert.Equal(Now.AddMinutes(15), result.ExpiresAt);
        Assert.Equal(Issuer, token.Issuer);
        Assert.Contains(Audience, token.Audiences);
        Assert.Equal(UserId.ToString(), token.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal("user@example.com", token.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.False(string.IsNullOrWhiteSpace(token.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Jti).Value));
        Assert.Equal(SecurityAlgorithms.HmacSha256, token.Header.Alg);
        Assert.NotEmpty(token.RawSignature);
        Assert.Equal(result.ExpiresAt.ToUnixTimeSeconds(), token.Payload.Expiration);
    }

    [Fact]
    public void Generate_TokenValidatesWithConfiguredKeyIssuerAudienceAndExpiration()
    {
        var result = CreateGenerator().Generate(new AccessTokenRequest(UserId, "user@example.com"));

        var principal = ValidateToken(result.Token, SigningKey);

        Assert.NotNull(principal);
    }

    [Fact]
    public void Generate_TokenValidationFailsWithWrongSigningKey()
    {
        var result = CreateGenerator().Generate(new AccessTokenRequest(UserId, "user@example.com"));

        Assert.ThrowsAny<SecurityTokenException>(() =>
            ValidateToken(result.Token, "different-test-only-signing-key-32-bytes-minimum"));
    }

    [Fact]
    public void Generate_TokenValidationFailsWithWrongIssuer()
    {
        var result = CreateGenerator().Generate(new AccessTokenRequest(UserId, "user@example.com"));

        Assert.Throws<SecurityTokenInvalidIssuerException>(() =>
            ValidateToken(result.Token, SigningKey, validIssuer: "DifferentIssuer"));
    }

    [Fact]
    public void Generate_TokenValidationFailsWithWrongAudience()
    {
        var result = CreateGenerator().Generate(new AccessTokenRequest(UserId, "user@example.com"));

        Assert.Throws<SecurityTokenInvalidAudienceException>(() =>
            ValidateToken(result.Token, SigningKey, validAudience: "DifferentAudience"));
    }

    [Fact]
    public void Generate_ExpiredTokenFailsValidation()
    {
        var result = CreateGenerator().Generate(new AccessTokenRequest(UserId, "user@example.com"));

        Assert.Throws<SecurityTokenInvalidLifetimeException>(() =>
            ValidateToken(result.Token, SigningKey, validationTime: Now.AddMinutes(16)));
    }

    [Fact]
    public void Generate_PayloadContainsNoSensitiveClaimsOrValues()
    {
        var result = CreateGenerator().Generate(new AccessTokenRequest(UserId, "user@example.com"));
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        var payload = string.Join('|', token.Claims.Select(claim => $"{claim.Type}:{claim.Value}"));
        string[] allowedClaims =
        [
            JwtRegisteredClaimNames.Sub,
            JwtRegisteredClaimNames.Email,
            JwtRegisteredClaimNames.Jti,
            JwtRegisteredClaimNames.Nbf,
            JwtRegisteredClaimNames.Exp,
            JwtRegisteredClaimNames.Iss,
            JwtRegisteredClaimNames.Aud
        ];

        Assert.All(token.Claims, claim => Assert.Contains(claim.Type, allowedClaims));
        Assert.DoesNotContain("password", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refresh", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tokenhash", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SigningKey, payload, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_WhenCalledTwice_ProducesDifferentTokensAndJtiClaims()
    {
        var generator = CreateGenerator();

        var first = generator.Generate(new AccessTokenRequest(UserId, "user@example.com"));
        var second = generator.Generate(new AccessTokenRequest(UserId, "user@example.com"));
        var handler = new JwtSecurityTokenHandler();
        var firstJti = handler.ReadJwtToken(first.Token).Id;
        var secondJti = handler.ReadJwtToken(second.Token).Id;

        Assert.NotEqual(first.Token, second.Token);
        Assert.NotEqual(firstJti, secondJti);
    }

    [Fact]
    public void Generate_WithEmptyUserId_ThrowsArgumentException()
    {
        var generator = CreateGenerator();

        Assert.Throws<ArgumentException>(() =>
            generator.Generate(new AccessTokenRequest(Guid.Empty, "user@example.com")));
    }

    [Fact]
    public void Generate_WithNullRequest_ThrowsArgumentNullException()
    {
        var generator = CreateGenerator();

        Assert.Throws<ArgumentNullException>(() => generator.Generate(null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Generate_WithInvalidEmail_ThrowsArgumentException(string? email)
    {
        var generator = CreateGenerator();

        Assert.Throws<ArgumentException>(() =>
            generator.Generate(new AccessTokenRequest(UserId, email!)));
    }

    [Fact]
    public void DependencyInjection_WithValidJwtConfiguration_PassesStartupValidationAndResolvesGenerator()
    {
        using var provider = CreateServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();
        var generator = provider.GetRequiredService<IAccessTokenGenerator>();

        Assert.NotNull(generator);
    }

    [Theory]
    [InlineData("Jwt:SigningKey", null)]
    [InlineData("Jwt:SigningKey", "short-key")]
    [InlineData("Jwt:Issuer", null)]
    [InlineData("Jwt:Audience", null)]
    [InlineData("Jwt:AccessTokenLifetime", "00:00:00")]
    [InlineData("Jwt:AccessTokenLifetime", "-00:01:00")]
    public void DependencyInjection_WithInvalidJwtConfiguration_FailsValidation(
        string key,
        string? value)
    {
        using var provider = CreateServiceProvider(new Dictionary<string, string?> { [key] = value });

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IStartupValidator>().Validate());
    }

    private static IAccessTokenGenerator CreateGenerator() =>
        CreateServiceProvider().GetRequiredService<IAccessTokenGenerator>();

    private static ServiceProvider CreateServiceProvider(
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=test;Database=test",
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = Audience,
            ["Jwt:SigningKey"] = SigningKey,
            ["Jwt:AccessTokenLifetime"] = "00:15:00"
        };

        if (overrides is not null)
        {
            foreach (var setting in overrides)
            {
                settings[setting.Key] = setting.Value;
            }
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));

        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal ValidateToken(
        string token,
        string signingKey,
        string validIssuer = Issuer,
        string validAudience = Audience,
        DateTimeOffset? validationTime = null)
    {
        var currentTime = validationTime ?? Now;
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = validIssuer,
            ValidateAudience = true,
            ValidAudience = validAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            LifetimeValidator = (notBefore, expires, _, _) =>
                notBefore <= currentTime.UtcDateTime && expires > currentTime.UtcDateTime,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        };

        return new JwtSecurityTokenHandler().ValidateToken(token, validationParameters, out _);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
