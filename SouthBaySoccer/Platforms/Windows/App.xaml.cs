using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace SouthBaySoccer.WinUI;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        InitializeComponent();

        var appInstance = AppInstance.GetCurrent();
        appInstance.Activated += (_, args) => ForwardProtocolActivation(args);
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()
            .TryEnqueue(() => ForwardProtocolActivation(appInstance.GetActivatedEventArgs()));
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    private static void ForwardProtocolActivation(AppActivationArguments activationArguments)
    {
        if (activationArguments.Kind != ExtendedActivationKind.Protocol
            || activationArguments.Data is not ProtocolActivatedEventArgs protocolArguments)
        {
            return;
        }

        Microsoft.Maui.Controls.Application.Current?
            .SendOnAppLinkRequestReceived(protocolArguments.Uri);
    }
}
