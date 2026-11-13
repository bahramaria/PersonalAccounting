using Hesabyar.Application.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hesabyar.Infrastructure.Persistence;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddHesabyarInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton<SqliteConnectionPragmaInterceptor>();
        services.AddDbContextFactory<AppDbContext>((serviceProvider, options) =>
        {
            options.UseSqlite(connectionString, sqlite => sqlite.CommandTimeout(30));
            options.AddInterceptors(serviceProvider.GetRequiredService<SqliteConnectionPragmaInterceptor>());
        });

        services.AddScoped<IAccountingRepository, EfAccountingRepository>();
        services.AddSingleton<DatabaseInitializer>();

        return services;
    }
}
