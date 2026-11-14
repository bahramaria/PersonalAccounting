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
    public static string CategoriesTab => Get(nameof(CategoriesTab));
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
    public static string EditAccount => Get(nameof(EditAccount));
    public static string ArchiveAccount => Get(nameof(ArchiveAccount));
    public static string ArchiveAccountConfirmation => Get(nameof(ArchiveAccountConfirmation));
    public static string AccountUpdateFailed => Get(nameof(AccountUpdateFailed));
    public static string AccountArchiveFailed => Get(nameof(AccountArchiveFailed));
    public static string CategoriesHeading => Get(nameof(CategoriesHeading));
    public static string AddCategory => Get(nameof(AddCategory));
    public static string CategoryTypePrompt => Get(nameof(CategoryTypePrompt));
    public static string ExpenseCategory => Get(nameof(ExpenseCategory));
    public static string IncomeCategory => Get(nameof(IncomeCategory));
    public static string CategoryEdit => Get(nameof(CategoryEdit));
    public static string CategoryArchive => Get(nameof(CategoryArchive));
    public static string CategoryArchiveConfirmation => Get(nameof(CategoryArchiveConfirmation));
    public static string CategoriesEmpty => Get(nameof(CategoriesEmpty));
    public static string CategoriesLoadFailed => Get(nameof(CategoriesLoadFailed));
    public static string CategoryCreateFailed => Get(nameof(CategoryCreateFailed));
    public static string CategoryUpdateFailed => Get(nameof(CategoryUpdateFailed));
    public static string CategoryArchiveFailed => Get(nameof(CategoryArchiveFailed));

    public static string TransactionsHeading => Get(nameof(TransactionsHeading));
    public static string AddTransaction => Get(nameof(AddTransaction));
    public static string TransactionAmountPrompt => Get(nameof(TransactionAmountPrompt));
    public static string TransactionAccountPrompt => Get(nameof(TransactionAccountPrompt));
    public static string TransactionDestinationPrompt => Get(nameof(TransactionDestinationPrompt));
    public static string TransactionCategoryPrompt => Get(nameof(TransactionCategoryPrompt));
    public static string TransactionDescriptionPrompt => Get(nameof(TransactionDescriptionPrompt));
    public static string TransactionDatePrompt => Get(nameof(TransactionDatePrompt));
    public static string TransactionsEmpty => Get(nameof(TransactionsEmpty));
    public static string TransactionLoadFailed => Get(nameof(TransactionLoadFailed));
    public static string TransactionCreateFailed => Get(nameof(TransactionCreateFailed));
    public static string TransactionDelete => Get(nameof(TransactionDelete));
    public static string TransactionDeleteConfirmation => Get(nameof(TransactionDeleteConfirmation));
    public static string TransactionEdit => Get(nameof(TransactionEdit));
    public static string TransactionDuplicate => Get(nameof(TransactionDuplicate));
    public static string Expense => Get(nameof(Expense));
    public static string Income => Get(nameof(Income));
    public static string Transfer => Get(nameof(Transfer));
    public static string TransactionSearchPlaceholder => Get(nameof(TransactionSearchPlaceholder));
    public static string TransactionAmountInvalid => Get(nameof(TransactionAmountInvalid));
    public static string TransactionDateInvalid => Get(nameof(TransactionDateInvalid));
    public static string UnknownAccount => Get(nameof(UnknownAccount));
    public static string TransactionFilters => Get(nameof(TransactionFilters));
    public static string TransactionFilterAll => Get(nameof(TransactionFilterAll));
    public static string TransactionFromDate => Get(nameof(TransactionFromDate));
    public static string TransactionToDate => Get(nameof(TransactionToDate));
    public static string TransactionAccountFilter => Get(nameof(TransactionAccountFilter));
    public static string TransactionCategoryFilter => Get(nameof(TransactionCategoryFilter));
    public static string TransactionMinimumAmount => Get(nameof(TransactionMinimumAmount));
    public static string TransactionMaximumAmount => Get(nameof(TransactionMaximumAmount));
    public static string TransactionDateRangeInvalid => Get(nameof(TransactionDateRangeInvalid));
    public static string TransactionAmountRangeInvalid => Get(nameof(TransactionAmountRangeInvalid));
    public static string TransactionSort => Get(nameof(TransactionSort));
    public static string TransactionSortNewest => Get(nameof(TransactionSortNewest));
    public static string TransactionSortOldest => Get(nameof(TransactionSortOldest));
    public static string TransactionClearFilters => Get(nameof(TransactionClearFilters));

    private static string Get(string name) => ResourceManager.GetString(name, Culture) ?? name;
}
