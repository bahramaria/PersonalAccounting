using Hesabyar.App.Resources.Strings;
using Hesabyar.App.Views;
using Hesabyar.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace Hesabyar.App;

public sealed class AppShell : Shell
{
    public AppShell(IServiceProvider services, ILocalizationService localization)
    {
        FlowDirection = ToMauiFlowDirection(localization.FlowDirection);

        var tabs = new TabBar();
        tabs.Items.Add(CreateTab(
            AppStrings.HomeTab,
            "home",
            () => services.GetRequiredService<DashboardPage>()));
        tabs.Items.Add(CreateTab(
            AppStrings.TransactionsTab,
            "transactions",
            () => services.GetRequiredService<TransactionsPage>()));
        tabs.Items.Add(CreateTab(
            AppStrings.SmsTab,
            "sms",
            () => services.GetRequiredService<BankSmsPage>()));
        tabs.Items.Add(CreateTab(
            AppStrings.ChequesTab,
            "cheques",
            () => services.GetRequiredService<ChequesPage>()));
        tabs.Items.Add(CreateTab(
            AppStrings.LoansTab,
            "loans",
            () => services.GetRequiredService<LoansPage>()));
        tabs.Items.Add(CreateTab(
            AppStrings.RemindersTab,
            "reminders",
            () => services.GetRequiredService<RemindersPage>()));
        tabs.Items.Add(CreateTab(
            AppStrings.PeopleTab,
            "people",
            () => services.GetRequiredService<PeoplePage>()));
        tabs.Items.Add(CreateTab(
            AppStrings.AccountsTab,
            "accounts",
            () => services.GetRequiredService<AccountsPage>()));
        tabs.Items.Add(CreateTab(
            AppStrings.CategoriesTab,
            "categories",
            () => services.GetRequiredService<CategoriesPage>()));
        tabs.Items.Add(CreateTab(
            AppStrings.ReportsTab,
            "reports",
            () => new FeaturePlaceholderPage(
                AppStrings.ReportsTab,
                AppStrings.ReportsPlaceholder,
                localization)));
        tabs.Items.Add(CreateTab(
            AppStrings.SettingsTab,
            "settings",
            () => services.GetRequiredService<SettingsPage>()));

        Items.Add(tabs);
    }

    private static Tab CreateTab(string title, string route, Func<Page> createPage)
    {
        var tab = new Tab { Title = title, Route = route };
        tab.Items.Add(new ShellContent
        {
            Route = $"{route}-content",
            ContentTemplate = new DataTemplate(createPage)
        });
        return tab;
    }

    private static Microsoft.Maui.FlowDirection ToMauiFlowDirection(TextDirection direction) =>
        direction == TextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft
            : Microsoft.Maui.FlowDirection.LeftToRight;
}

public sealed class FeaturePlaceholderPage : ContentPage
{
    public FeaturePlaceholderPage(
        string title,
        string description,
        ILocalizationService localization)
    {
        Title = title;
        FlowDirection = localization.FlowDirection == TextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft
            : Microsoft.Maui.FlowDirection.LeftToRight;

        Content = new VerticalStackLayout
        {
            Padding = 24,
            Spacing = 12,
            Children =
            {
                new Label { Text = title, FontSize = 24, FontAttributes = FontAttributes.Bold },
                new Label { Text = description, FontSize = 16 }
            }
        };
    }
}
