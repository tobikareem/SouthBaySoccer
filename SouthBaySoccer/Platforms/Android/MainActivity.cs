using Android.App;
using Android.Content;
using Android.Content.PM;

namespace SouthBaySoccer;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize
                           | ConfigChanges.Orientation
                           | ConfigChanges.UiMode
                           | ConfigChanges.ScreenLayout
                           | ConfigChanges.SmallestScreenSize
                           | ConfigChanges.Density)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "southbaysoccer",
    DataHost = "auth",
    DataPathPrefixes = ["/whatsapp", "/register", "/login"])]
// Android App Links for the Pickup Pal bot's reply links; verified against
// https://n9jabay.desolatravels.com/.well-known/assetlinks.json (AUTH-10 M13.1).
[IntentFilter(
    [Intent.ActionView],
    AutoVerify = true,
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "https",
    DataHost = "n9jabay.desolatravels.com",
    DataPathPrefixes = ["/register", "/login"])]
public class MainActivity : MauiAppCompatActivity;
