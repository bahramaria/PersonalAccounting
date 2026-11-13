using Hesabyar.Application.Accounts;
using Hesabyar.Application.Common;
using Hesabyar.App.Services;
using Hesabyar.App.ViewModels;
using Hesabyar.App.Views;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Time;
using Hesabyar.Localization;
using Microsoft.Data.Sqlite;
using Microsoft.Maui.Storage;

namespace Hesabyar.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        AppLocalization.Initialize();

        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<ILocalizationService, CultureLocalizationService>();

        var databasePath = Path.Combine(FileSystem.AppDataDirectory, "hesabyar.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Pooling = true
        }.ToString();

        builder.Services.AddHesabyarInfrastructure(connectionString);
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<StartupPage>();
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<AccountService>();
        builder.Services.AddTransient<AccountsViewModel>();
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<AccountsPage>();

        return builder.Build();
    }
}
