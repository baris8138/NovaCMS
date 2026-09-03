using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using NovaCMS.Application.Security;
using NovaCMS.Infrastructure.Persistence;
using NovaCMS.Infrastructure.Security;

namespace NovaCMS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' was not found.");

        services.AddDbContext<NovaCmsDbContext>(options =>
            options.UseNpgsql(connectionString));
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer), "Jwt:Issuer is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Audience), "Jwt:Audience is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.SigningKey), "Jwt:SigningKey is required.")
            .Validate(
                options => string.IsNullOrWhiteSpace(options.SigningKey) ||
                    Encoding.UTF8.GetByteCount(options.SigningKey) >= 32,
                "Jwt:SigningKey must contain at least 256 bits (32 UTF-8 bytes) of key material.")
            .Validate(options => options.AccessTokenLifetime > TimeSpan.Zero,
                "Jwt:AccessTokenLifetime must be greater than zero.")
            .ValidateOnStart();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAccessTokenGenerator, JwtAccessTokenGenerator>();

        return services;
    }
}
