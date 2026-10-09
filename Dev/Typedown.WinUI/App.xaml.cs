using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Typedown.WinUI.Activation;
using Typedown.WinUI.Services;
using Typedown.WinUI.Windowing;

namespace Typedown.WinUI;

public partial class App : Application
{
    private readonly AppActivationArguments? initialActivation;
    private readonly string[] processArguments;
    private readonly AppActivationBroker activationBroker;
    private readonly CancellationTokenSource activationLifetime = new();
    private readonly ServiceProvider processServices;
    private readonly WindowManager windowManager;
    private WinUiAppActivationService? activationService;

    public App()
        : this(
            initialActivation: null,
            processArguments: Array.Empty<string>(),
            activationBroker: new AppActivationBroker(AppInstance.GetCurrent()))
    {
    }

    internal App(
        AppActivationArguments? initialActivation,
        string[] processArguments,
        AppActivationBroker activationBroker)
    {
        this.initialActivation = initialActivation;
        this.processArguments = processArguments
            ?? throw new ArgumentNullException(nameof(processArguments));
        this.activationBroker = activationBroker
            ?? throw new ArgumentNullException(nameof(activationBroker));
        InitializeComponent();
        var appName = typeof(App).Assembly.GetName().Name
            ?? throw new InvalidOperationException("The application assembly has no name.");
        processServices = ServiceConfiguration.BuildProcessProvider(appName);
        windowManager = processServices.GetRequiredService<WindowManager>();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (activationService is not null)
        {
            return;
        }

        activationService = new WinUiAppActivationService(
            windowManager,
            activationBroker,
            Program.NewWindowArgument);
        var initialRequest = activationService.CreateInitialRequest(
            initialActivation,
            args.Arguments,
            processArguments);

        _ = ProcessInitialActivationAsync(
            activationService,
            initialRequest,
            activationLifetime.Token);

        _ = ProcessRedirectedActivationsAsync(
            activationService,
            activationLifetime.Token);
    }

    private async Task ProcessInitialActivationAsync(
        WinUiAppActivationService service,
        Typedown.Contracts.Platform.AppActivationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await service.ActivateAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Initial application activation failed: {exception}");
            if (windowManager.Count == 0)
            {
                windowManager.CreateWindow();
            }
        }
    }

    private static async Task ProcessRedirectedActivationsAsync(
        WinUiAppActivationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var request in service.ListenAsync(cancellationToken))
            {
                try
                {
                    await service.ActivateAsync(request, cancellationToken);
                }
                catch (Exception exception)
                    when (exception is not OperationCanceledException
                        || !cancellationToken.IsCancellationRequested)
                {
                    Debug.WriteLine($"Redirected application activation failed: {exception}");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Application activation pump failed: {exception}");
        }
    }
}
