using Hesabyar.Application.Accounts;
using Hesabyar.Application.People;
using Hesabyar.Application.Cheques;
using Hesabyar.Application.Loans;
using Hesabyar.Application.Transactions;
using Hesabyar.Application.Common;
using Hesabyar.App.Services;
using Hesabyar.App.ViewModels;
using Hesabyar.App.Views;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Time;
using Hesabyar.Localization;
using Hesabyar.Localization.Calendar;
using Hesabyar.Localization.Numbers;
using Microsoft.Data.Sqlite;
using Microsoft.Maui.Storage;
using Microsoft.EntityFrameworkCore;
using Hesabyar.Infrastructure.Security;

namespace Hesabyar.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        SQLitePCL.Batteries_V2.Init();
        builder.UseMauiApp<App>();
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<ILocalizationService, CultureLocalizationService>();
        builder.Services.AddSingleton<ICalendarService, PersianCalendarService>();
        builder.Services.AddSingleton<IDateCalendarService, DateCalendarService>();
        builder.Services.AddSingleton<INumberNormalizer, NumberNormalizer>();
        builder.Services.AddSingleton<AppLockService>();
        builder.Services.AddSingleton<ReminderNotificationSyncService>();
#if ANDROID
        builder.Services.AddSingleton<IReminderNotificationScheduler, AndroidReminderNotificationScheduler>();
#elif WINDOWS
        builder.Services.AddSingleton<IReminderNotificationScheduler, WindowsReminderNotificationScheduler>();
#endif

        var databasePath = Path.Combine(FileSystem.AppDataDirectory, "hesabyar.db");
        builder.Services.AddSingleton<IDeviceSecretStore, MauiSecureStorageAdapter>();
        builder.Services.AddSingleton<DatabaseKeyProvider>(provider =>
            new DatabaseKeyProvider(databasePath, provider.GetRequiredService<IDeviceSecretStore>()));
        builder.Services.AddSingleton<IDatabaseKeyProvider>(provider =>
            provider.GetRequiredService<DatabaseKeyProvider>());
        builder.Services.AddSingleton<IDatabaseKeyInitializer>(provider =>
            provider.GetRequiredService<DatabaseKeyProvider>());
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, ForeignKeys = true, Pooling = true
        }.ToString();
        builder.Services.AddHesabyarInfrastructure(connectionString);
        builder.Services.AddSingleton(provider => new BackupService(
            provider.GetRequiredService<IDbContextFactory<AppDbContext>>(),
            databasePath,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IDatabaseKeyProvider>()));
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<StartupPage>();
        builder.Services.AddTransient<PinLockPage>();
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<AccountService>();
        builder.Services.AddTransient<PersonService>();
        builder.Services.AddTransient<ChequeService>();
        builder.Services.AddTransient<LoanService>();
        builder.Services.AddTransient<LoansViewModel>();
        builder.Services.AddTransient<LoansPage>();
        builder.Services.AddTransient<RemindersViewModel>();
        builder.Services.AddTransient<RemindersPage>();
        builder.Services.AddTransient<RecurringTransactionsViewModel>();
        builder.Services.AddTransient<RecurringTransactionsPage>();
        builder.Services.AddTransient<BankSmsPage>();
        builder.Services.AddTransient<ChequesViewModel>();
        builder.Services.AddTransient<ChequesPage>();
        builder.Services.AddTransient<PeopleViewModel>();
        builder.Services.AddTransient<PeoplePage>();
        builder.Services.AddTransient<TransactionService>();
        builder.Services.AddTransient<AccountsViewModel>();
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddSingleton<ReportsExportService>();
        builder.Services.AddTransient<ReportsPage>();
        builder.Services.AddTransient<AccountsPage>();
        builder.Services.AddTransient<TransactionsViewModel>();
        builder.Services.AddTransient<TransactionsPage>();
        builder.Services.AddTransient<CategoriesViewModel>();
        builder.Services.AddTransient<CategoriesPage>();
        builder.Services.AddTransient<SettingsPage>();
        return builder.Build();
    }
}