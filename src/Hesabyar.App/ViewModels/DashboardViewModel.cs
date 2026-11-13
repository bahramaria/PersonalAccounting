using CommunityToolkit.Mvvm.ComponentModel;
using Hesabyar.App.Resources.Strings;

namespace Hesabyar.App.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    [ObservableProperty]
    private string _heading = AppStrings.HomeHeading;

    [ObservableProperty]
    private string _description = AppStrings.HomeDescription;

    [ObservableProperty]
    private string _balanceMessage = AppStrings.BalanceUnavailable;
}
