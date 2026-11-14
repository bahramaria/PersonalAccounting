using Hesabyar.App.Resources.Strings;
using Hesabyar.App.ViewModels;
using Hesabyar.Localization;

namespace Hesabyar.App.Views;

public partial class DashboardPage : ContentPage
{
    private readonly DashboardViewModel _viewModel;

    public DashboardPage(DashboardViewModel viewModel, ILocalizationService localization)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        FlowDirection = localization.FlowDirection == TextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft
            : Microsoft.Maui.FlowDirection.LeftToRight;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnTransactionsClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//transactions");

    private async void OnQuickAddClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//transactions");
        if (Shell.Current.CurrentPage is TransactionsPage transactionsPage)
            await transactionsPage.OpenQuickAddAsync();
    }
}
