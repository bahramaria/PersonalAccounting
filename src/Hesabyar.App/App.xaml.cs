using Microsoft.Extensions.DependencyInjection;
using MauiApplication = Microsoft.Maui.Controls.Application;

namespace Hesabyar.App;

public partial class App : MauiApplication
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(_services.GetRequiredService<StartupPage>());
}
