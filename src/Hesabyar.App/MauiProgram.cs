using Hesabyar.Application.Accounts;
using Hesabyar.Application.Transactions;
using Hesabyar.Application.Common;
using Hesabyar.App.ViewModels;
using Hesabyar.App.Views;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Time;
using Hesabyar.Localization;
using Hesabyar.Localization.Calendar;
using Microsoft.Data.Sqlite;
using Microsoft.Maui.Storage;

namespace Hesabyar.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<ILocalizationService, CultureLocalizationService>();
        builder.Services.AddSingleton<ICalendarService, PersianCalendarService>();

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
        builder.Services.AddTransient<TransactionService>();
        builder.Services.AddTransient<AccountsViewModel>();
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<AccountsPage>();
        builder.Services.AddTransient<TransactionsViewModel>();
        builder.Services.AddTransient<TransactionsPage>();
        builder.Services.AddTransient<CategoriesViewModel>();
        builder.Services.AddTransient<CategoriesPage>();
        builder.Services.AddTransient<SettingsPage>();

        return builder.Build();
    }
}
