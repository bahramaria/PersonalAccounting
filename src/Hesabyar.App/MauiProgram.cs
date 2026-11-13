using Hesabyar.App.Services;
using Hesabyar.App.ViewModels;
using Hesabyar.App.Views;
using Microsoft.Extensions.Logging;

namespace Hesabyar.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        AppLocalization.Initialize();

        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<DashboardPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
