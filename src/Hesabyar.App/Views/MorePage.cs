using Hesabyar.App.Resources.Strings;
using Hesabyar.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace Hesabyar.App.Views;

public sealed class MorePage : ContentPage
{
    public MorePage(IServiceProvider services, ILocalizationService localization)
    {
        Title = AppStrings.MoreTab;
        FlowDirection = localization.FlowDirection == TextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft
            : Microsoft.Maui.FlowDirection.LeftToRight;

        var content = new VerticalStackLayout
        {
            Padding = new Thickness(20, 24),
            Spacing = 12
        };
        content.Children.Add(new Label
        {
            Text = AppStrings.MoreTab,
            FontSize = 24,
            FontAttributes = FontAttributes.Bold
        });

        AddNavigationButton(content, AppStrings.AccountsTab, () => services.GetRequiredService<AccountsPage>());
        AddNavigationButton(content, AppStrings.AssetsHeading, () => services.GetRequiredService<PhysicalAssetsPage>());
        AddNavigationButton(content, AppStrings.CategoriesTab, () => services.GetRequiredService<CategoriesPage>());
        AddNavigationButton(content, AppStrings.PeopleTab, () => services.GetRequiredService<PeoplePage>());
        AddNavigationButton(content, AppStrings.ChequesTab, () => services.GetRequiredService<ChequesPage>());
        AddNavigationButton(content, AppStrings.LoansTab, () => services.GetRequiredService<LoansPage>());
        AddNavigationButton(content, AppStrings.RemindersTab, () => services.GetRequiredService<RemindersPage>());
        AddNavigationButton(content, AppStrings.RecurringTab, () => services.GetRequiredService<RecurringTransactionsPage>());
        AddNavigationButton(content, AppStrings.ReportsTab, () => services.GetRequiredService<ReportsPage>());
        AddNavigationButton(content, AppStrings.PlanningTab, () => services.GetRequiredService<PlanningPage>());
        AddNavigationButton(content, AppStrings.SettingsTab, () => services.GetRequiredService<SettingsPage>());

        Content = new ScrollView { Content = content };
    }

    private void AddNavigationButton(VerticalStackLayout parent, string title, Func<Page> createPage)
    {
        var button = new Button
        {
            Text = title,
            HorizontalOptions = LayoutOptions.Fill,
            MinimumHeightRequest = 48
        };
        button.Clicked += async (_, _) => await Navigation.PushAsync(createPage());
        parent.Children.Add(button);
    }
}
