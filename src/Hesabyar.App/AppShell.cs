using Hesabyar.App.Resources.Strings;
using Hesabyar.App.Services;
using Hesabyar.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Hesabyar.App;

public sealed class AppShell : Shell
{
    public AppShell(IServiceProvider services)
    {
        FlowDirection = AppLocalization.CurrentFlowDirection;

        var tabs = new TabBar();
        tabs.Items.Add(CreateTab(AppStrings.HomeTab, "home", () => services.GetRequiredService<DashboardPage>()));
        tabs.Items.Add(CreateTab(AppStrings.TransactionsTab, "transactions",
            () => new FeaturePlaceholderPage(AppStrings.TransactionsTab, AppStrings.TransactionsPlaceholder)));
        tabs.Items.Add(CreateTab(AppStrings.AccountsTab, "accounts",
            () => new FeaturePlaceholderPage(AppStrings.AccountsTab, AppStrings.AccountsPlaceholder)));
        tabs.Items.Add(CreateTab(AppStrings.ReportsTab, "reports",
            () => new FeaturePlaceholderPage(AppStrings.ReportsTab, AppStrings.ReportsPlaceholder)));
        tabs.Items.Add(CreateTab(AppStrings.MoreTab, "more",
            () => new FeaturePlaceholderPage(AppStrings.MoreTab, AppStrings.MorePlaceholder)));

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
}

public sealed class FeaturePlaceholderPage : ContentPage
{
    public FeaturePlaceholderPage(string title, string description)
    {
        Title = title;
        FlowDirection = AppLocalization.CurrentFlowDirection;
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
