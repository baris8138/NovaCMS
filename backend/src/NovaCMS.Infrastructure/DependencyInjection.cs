using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        return services;
    }
}
