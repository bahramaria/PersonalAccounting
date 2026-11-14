using Microsoft.Extensions.DependencyInjection;
using Hesabyar.App.Services;
using Microsoft.Maui.Storage;

namespace Hesabyar.App;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        AppLocalization.Initialize();
        _services = services;
        UserAppTheme = Preferences.Get("app.theme", "system") switch
        {
            "light" => AppTheme.Light,
            "dark" => AppTheme.Dark,
            _ => AppTheme.Unspecified
        };
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(_services.GetRequiredService<StartupPage>());
}
