using Android.App;
using Android.Content;
using Android.Content.PM;
using Hesabyar.App.Views;

namespace Hesabyar.App;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize |
                           ConfigChanges.Orientation |
                           ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout |
                           ConfigChanges.SmallestScreenSize |
                           ConfigChanges.Density)]
[IntentFilter(new[] { Intent.ActionSend }, Categories = new[] { Intent.CategoryDefault }, DataMimeType = "text/plain")]
public sealed class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        CaptureSharedText(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent is not null)
            CaptureSharedText(intent);
    }

    private static void CaptureSharedText(Intent? intent)
    {
        if (intent?.Action == Intent.ActionSend)
            SharedSmsContent.Set(intent.GetStringExtra(Intent.ExtraText));
    }
}
