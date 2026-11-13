using Hesabyar.App.ViewModels;
using Hesabyar.Localization;

namespace Hesabyar.App.Views;

public partial class DashboardPage : ContentPage
{
    public DashboardPage(DashboardViewModel viewModel, ILocalizationService localization)
    {
        InitializeComponent();
        BindingContext = viewModel;
        FlowDirection = localization.FlowDirection == TextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft
            : Microsoft.Maui.FlowDirection.LeftToRight;
    }
}
