using CommunityToolkit.Mvvm.ComponentModel;
using Hesabyar.App.Resources.Strings;

namespace Hesabyar.App.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private string _heading = AppStrings.HomeHeading;
    private string _description = AppStrings.HomeDescription;
    private string _balanceMessage = AppStrings.BalanceUnavailable;

    public string Heading
    {
        get => _heading;
        private set => SetProperty(ref _heading, value);
    }

    public string Description
    {
        get => _description;
        private set => SetProperty(ref _description, value);
    }

    public string BalanceMessage
    {
        get => _balanceMessage;
        private set => SetProperty(ref _balanceMessage, value);
    }
}
