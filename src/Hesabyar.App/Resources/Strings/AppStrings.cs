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

    public static string AccountsHeading => Get(nameof(AccountsHeading));
    public static string AddAccount => Get(nameof(AddAccount));
    public static string AccountNamePrompt => Get(nameof(AccountNamePrompt));
    public static string Save => Get(nameof(Save));
    public static string Cancel => Get(nameof(Cancel));
    public static string AccountKindPrompt => Get(nameof(AccountKindPrompt));
    public static string AccountKindCash => Get(nameof(AccountKindCash));
    public static string AccountKindBank => Get(nameof(AccountKindBank));
    public static string AccountKindCard => Get(nameof(AccountKindCard));
    public static string AccountKindWallet => Get(nameof(AccountKindWallet));
    public static string AccountKindCreditCard => Get(nameof(AccountKindCreditCard));
    public static string OpeningBalance => Get(nameof(OpeningBalance));
    public static string OpeningBalancePrompt => Get(nameof(OpeningBalancePrompt));
    public static string Skip => Get(nameof(Skip));
    public static string ValidationError => Get(nameof(ValidationError));
    public static string OpeningBalanceInvalid => Get(nameof(OpeningBalanceInvalid));
    public static string Ok => Get(nameof(Ok));
    public static string CurrencyRial => Get(nameof(CurrencyRial));
    public static string AccountsLoadFailed => Get(nameof(AccountsLoadFailed));
    public static string AccountsEmpty => Get(nameof(AccountsEmpty));
    public static string AccountCreateFailed => Get(nameof(AccountCreateFailed));

    private static string Get(string name) => ResourceManager.GetString(name, Culture) ?? name;
}
