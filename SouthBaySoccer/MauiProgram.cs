using CommunityToolkit.Maui;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Services;
using SouthBaySoccer.Services.Authentication;
using SouthBaySoccer.Services.Clients;
using SouthBaySoccer.Services.Leaderboard;
using SouthBaySoccer.Services.Navigation;
using SouthBaySoccer.Services.Players;
using SouthBaySoccer.Services.Profile;
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
                fonts.AddFont("Inter-Regular.ttf", "InterRegular");
                fonts.AddFont("Inter-SemiBold.ttf", "InterSemibold");
                fonts.AddFont("Inter-Bold.ttf", "InterBold");
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
        builder.Services.AddSingleton<StartupErrorHandler>();
        builder.Services.AddSingleton<IUserDialogService, UserDialogService>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<MainPageModel>();
        builder.Services.AddSingleton<ProjectListPageModel>();
        builder.Services.AddSingleton<ManageMetaPageModel>();

        builder.Services.AddTransient<AppShell>();
        builder.Services.AddTransient<WelcomeBackPage>();
        builder.Services.AddTransient<WelcomeBackPageModel>();

        builder.Services.AddTransient<SessionsHomePage>();
        builder.Services.AddTransient<SessionsHomePageModel>();
        builder.Services.AddTransient<GameDayPage>();
        builder.Services.AddTransient<GameDayPageModel>();
        builder.Services.AddTransient<CaptainAssignmentPageModel>();
        builder.Services.AddTransient<TeamDraftPageModel>();
        builder.Services.AddTransient<PostGameApprovalPageModel>();
        builder.Services.AddLeaderboardFeature();
        builder.Services.AddPlayersFeature();
        builder.Services.AddProfileFeature();
        builder.Services.AddSingleton<ISessionsNavigator, ShellSessionsNavigator>();
        builder.Services.AddSingleton<IGameDayNavigator, ShellGameDayNavigator>();
        builder.Services.AddSingleton<IMatchStatsNavigator, ShellMatchStatsNavigator>();
        builder.Services.AddSingleton(new MatchStatsOptions());
        builder.Services.AddSingleton(new RateTeammatesOptions());
        builder.Services.AddSingleton<IRateTeammatesNavigator, ShellRateTeammatesNavigator>();

        // Wires the UserSecretsId from the csproj as an optional local configuration source.
        // This reads secrets when present on the running machine, but does not embed them into the
        // app package. Deployed mobile packages still need deploy-time configuration.
        builder.Configuration.AddUserSecrets<App>(optional: true);

        // Environment variables are useful for CI/simulator launches and win over user secrets.
        builder.Configuration.AddEnvironmentVariables();

        var defaultApiBaseUrl =
#if DEBUG && ANDROID
            PickupPalOptions.AndroidDebugApiBaseUrl;
#else
            PickupPalOptions.DefaultApiBaseUrl;
#endif
        var pickupPalOptions = PickupPalOptions.FromConfiguration(
            builder.Configuration,
            defaultApiBaseUrl);

        // Config-first: an explicit ClientDataSource setting always wins. Debug defaults to Seed for
        // deterministic local demos; Release defaults to Api because seed clients are excluded from
        // Release builds.
        var configuredClientDataSource = builder.Configuration["ClientDataSource"];
        var defaultClientDataSource =
#if RELEASE
            ClientDataSource.Api;
#else
            ClientDataSource.Seed;
#endif
        var clientDataSourceOptions = ClientDataSourceOptions.FromValue(
            configuredClientDataSource,
            defaultClientDataSource);
        builder.Services.AddSingleton(pickupPalOptions);
        builder.Services.AddSouthBaySoccerClients(
            clientDataSourceOptions,
            pickupPalOptions);
        builder.Services.AddSingleton<ISecureTokenStore, SecureTokenStore>();
        builder.Services.AddSingleton<IAuthenticationNavigator, AuthenticationNavigator>();
        builder.Services.AddSingleton<IAuthenticationCoordinator, AuthenticationCoordinator>();
        builder.Services.AddSingleton<IAppStartupService, AppStartupService>();
        builder.Services.AddSingleton<IExternalLauncher, ExternalLauncher>();

        builder.Services.AddTransientWithShellRoute<SessionDetailPage, SessionDetailPageModel>("session");
        builder.Services.AddTransientWithShellRoute<SchedulePage, SchedulePageModel>("schedule");
        builder.Services.AddTransientWithShellRoute<CreateSessionPage, CreateSessionPageModel>("create-session");
        builder.Services.AddTransientWithShellRoute<CaptainAssignmentPage, CaptainAssignmentPageModel>("captains");
        builder.Services.AddTransientWithShellRoute<TeamDraftPage, TeamDraftPageModel>("draft");
        builder.Services.AddTransientWithShellRoute<PostGameApprovalPage, PostGameApprovalPageModel>("postgame");
        builder.Services.AddTransientWithShellRoute<RecentGamesPage, RecentGamesPageModel>("recent-games");
        builder.Services.AddTransientWithShellRoute<MatchStatsPage, MatchStatsPageModel>("matchstats");
        builder.Services.AddTransientWithShellRoute<RateTeammatesPage, RateTeammatesPageModel>("rate-teammates");

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
