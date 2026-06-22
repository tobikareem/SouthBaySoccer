using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Services.Authentication;
using SouthBaySoccer.Services.Clients;
using SouthBaySoccer.Services.Navigation;
using Syncfusion.Maui.Toolkit.Hosting;

namespace SouthBaySoccer;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureSyncfusionToolkit()
            .ConfigureMauiHandlers(handlers =>
            {
#if WINDOWS
                Microsoft.Maui.Controls.Handlers.Items.CollectionViewHandler.Mapper.AppendToMapping(
                    "KeyboardAccessibleCollectionView",
                    (handler, _) =>
                    {
                        handler.PlatformView.SingleSelectionFollowsFocus = false;
                    });

                Microsoft.Maui.Handlers.ContentViewHandler.Mapper.AppendToMapping(
                    nameof(Pages.Controls.CategoryChart),
                    (handler, view) =>
                    {
                        if (view is Pages.Controls.CategoryChart
                            && handler.PlatformView is Microsoft.Maui.Platform.ContentPanel contentPanel)
                        {
                            contentPanel.IsTabStop = true;
                        }
                    });
#endif
            })
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                fonts.AddFont("SegoeUI-Semibold.ttf", "SegoeSemibold");
                fonts.AddFont("FluentSystemIcons-Regular.ttf", FluentUI.FontFamily);
                fonts.AddFont("Font Awesome 6 Free-Solid-900.otf", "FontAwesomeSolid");
                fonts.AddFont("Font Awesome 6 Brands-Regular-400.otf", "FontAwesomeBrands");
            })
            .ConfigureLifecycleEvents(lifecycle =>
            {
#if ANDROID
                lifecycle.AddAndroid(android =>
                {
                    android.OnCreate((activity, _) => ForwardAppLink(activity.Intent));
                    android.OnNewIntent((_, intent) => ForwardAppLink(intent));
                });
#endif
            });

#if DEBUG
        builder.Logging.AddDebug();
        builder.Services.AddLogging(configure => configure.AddDebug());
#endif

        builder.Services.AddSingleton<ProjectRepository>();
        builder.Services.AddSingleton<TaskRepository>();
        builder.Services.AddSingleton<CategoryRepository>();
        builder.Services.AddSingleton<TagRepository>();
        builder.Services.AddSingleton<SeedDataService>();
        builder.Services.AddSingleton<ModalErrorHandler>();
        builder.Services.AddSingleton<MainPageModel>();
        builder.Services.AddSingleton<ProjectListPageModel>();
        builder.Services.AddSingleton<ManageMetaPageModel>();

        builder.Services.AddTransient<AppShell>();
        builder.Services.AddTransient<WelcomeBackPage>();
        builder.Services.AddTransient<WelcomeBackPageModel>();

        builder.Services.AddTransient<SessionsHomePage>();
        builder.Services.AddTransient<SessionsHomePageModel>();
        builder.Services.AddTransient<StatsPage>();
        builder.Services.AddTransient<ProfilePage>();
        builder.Services.AddSingleton<ISessionsNavigator, ShellSessionsNavigator>();

        var pickupPalOptions = new PickupPalOptions();
        builder.Services.AddSingleton(pickupPalOptions);
        builder.Services.AddSouthBaySoccerClients(
            ClientDataSourceOptions.FromValue(builder.Configuration["ClientDataSource"]),
            pickupPalOptions);
        builder.Services.AddSingleton<ISecureTokenStore, SecureTokenStore>();
        builder.Services.AddSingleton<IAuthenticationNavigator, AuthenticationNavigator>();
        builder.Services.AddSingleton<IAuthenticationCoordinator, AuthenticationCoordinator>();
        builder.Services.AddSingleton<IAppStartupService, AppStartupService>();
        builder.Services.AddSingleton<IExternalLauncher, ExternalLauncher>();

        builder.Services.AddTransientWithShellRoute<SessionDetailPage, SessionDetailPageModel>("session");

        builder.Services.AddTransientWithShellRoute<ProjectDetailPage, ProjectDetailPageModel>("project");
        builder.Services.AddTransientWithShellRoute<TaskDetailPage, TaskDetailPageModel>("task");

        return builder.Build();
    }

#if ANDROID
    private static void ForwardAppLink(Android.Content.Intent? intent)
    {
        if (intent?.Action != Android.Content.Intent.ActionView)
        {
            return;
        }

        var data = intent.Data?.ToString();
        if (Uri.TryCreate(data, UriKind.Absolute, out var uri))
        {
            Application.Current?.SendOnAppLinkRequestReceived(uri);
        }
    }
#endif
}
