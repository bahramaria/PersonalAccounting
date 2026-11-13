using System.Globalization;
using System.Resources;

namespace Hesabyar.App.Resources.Strings;

public static class AppStrings
{
    private static readonly ResourceManager ResourceManager = new(
        "Hesabyar.App.Resources.Strings.AppResources",
        typeof(AppStrings).Assembly);

    public static CultureInfo? Culture { get; set; }

    public static string HomeTab => Get(nameof(HomeTab));
    public static string TransactionsTab => Get(nameof(TransactionsTab));
    public static string AccountsTab => Get(nameof(AccountsTab));
    public static string ReportsTab => Get(nameof(ReportsTab));
    public static string MoreTab => Get(nameof(MoreTab));
    public static string HomeHeading => Get(nameof(HomeHeading));
    public static string HomeDescription => Get(nameof(HomeDescription));
    public static string BalanceUnavailable => Get(nameof(BalanceUnavailable));
    public static string TransactionsPlaceholder => Get(nameof(TransactionsPlaceholder));
    public static string AccountsPlaceholder => Get(nameof(AccountsPlaceholder));
    public static string ReportsPlaceholder => Get(nameof(ReportsPlaceholder));
    public static string MorePlaceholder => Get(nameof(MorePlaceholder));
    public static string StartupInitializing => Get(nameof(StartupInitializing));
    public static string StartupFailed => Get(nameof(StartupFailed));
    public static string Retry => Get(nameof(Retry));

    private static string Get(string name) => ResourceManager.GetString(name, Culture) ?? name;
}
