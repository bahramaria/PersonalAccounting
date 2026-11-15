using Hesabyar.Application.Accounts;
using Hesabyar.Application.People;
using Hesabyar.Application.Cheques;
using Hesabyar.Application.Loans;
using Hesabyar.Infrastructure.Security;
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

        services.AddSingleton<SqliteConnectionPragmaInterceptor>(serviceProvider =>
            new SqliteConnectionPragmaInterceptor(serviceProvider.GetService<IDatabaseKeyProvider>()));
        services.AddDbContextFactory<AppDbContext>((serviceProvider, options) =>
        {
            options.UseSqlite(connectionString, sqlite => sqlite.CommandTimeout(30));
            options.AddInterceptors(serviceProvider.GetRequiredService<SqliteConnectionPragmaInterceptor>());
        });

        services.AddScoped<IAccountingRepository, EfAccountingRepository>();
        services.AddScoped<IPeopleRepository, PeopleRepository>();
        services.AddScoped<IChequeRepository, ChequesRepository>();
        services.AddScoped<ILoanRepository, LoanRepository>();
        services.AddSingleton<DatabaseInitializer>();

        return services;
    }
}
