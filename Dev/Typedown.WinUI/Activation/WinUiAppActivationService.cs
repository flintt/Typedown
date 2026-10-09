using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using Typedown.Contracts.Platform;
using Typedown.WinUI.Windowing;

namespace Typedown.WinUI.Activation;

internal sealed class WinUiAppActivationService : IAppActivationService, IDisposable
{
    private readonly WindowManager windowManager;
    private readonly DispatcherQueue dispatcherQueue;
    private readonly AppActivationBroker activationBroker;
    private readonly AppActivationNormalizer normalizer;
    private int isDisposed;
    private int isListening;

    public WinUiAppActivationService(
        WindowManager windowManager,
        AppActivationBroker activationBroker,
        string newWindowArgument)
    {
        this.windowManager = windowManager
            ?? throw new ArgumentNullException(nameof(windowManager));
        this.activationBroker = activationBroker
            ?? throw new ArgumentNullException(nameof(activationBroker));
        normalizer = new AppActivationNormalizer(newWindowArgument);
        dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException(
                "The activation service must be created on the UI thread.");
    }

    public AppActivationRequest CreateInitialRequest(
        AppActivationArguments? activation,
        string? launchArguments,
        IReadOnlyList<string> processArguments) =>
        normalizer.NormalizeInitial(activation, launchArguments, processArguments);

    public ValueTask<AppActivationResult> ActivateAsync(
        AppActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) != 0, this);

        if (dispatcherQueue.HasThreadAccess)
        {
            return ValueTask.FromResult(ActivateOnUiThread(request));
        }

        return new ValueTask<AppActivationResult>(
            DispatchToUiAsync(request, cancellationToken));
    }

    public async IAsyncEnumerable<AppActivationRequest> ListenAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) != 0, this);
        if (Interlocked.Exchange(ref isListening, 1) != 0)
        {
            throw new InvalidOperationException(
                "Only one redirected activation listener is supported.");
        }

        await foreach (var activation in activationBroker
            .ReadAllAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            yield return normalizer.Normalize(
                activation,
                AppActivationSource.Redirected);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        activationBroker.Dispose();
    }

    private AppActivationResult ActivateOnUiThread(AppActivationRequest request)
    {
        WindowSession? target = null;
        if (!request.OpenInNewWindow && request.PreferredWindowId is { } preferredId)
        {
            windowManager.TryGetSession(preferredId, out target);
        }

        if (!request.OpenInNewWindow && target is null)
        {
            target = windowManager.Sessions.FirstOrDefault(session => session.Context.IsActive)
                ?? windowManager.Sessions.LastOrDefault();
        }

        if (request.OpenInNewWindow || target is null)
        {
            target = windowManager.CreateWindow();
        }
        else
        {
            target.Window.Activate();
        }

        return new AppActivationResult
        {
            Status = AppActivationStatus.Handled,
            WindowId = target.Context.Id,
        };
    }

    private async Task<AppActivationResult> DispatchToUiAsync(
        AppActivationRequest request,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<AppActivationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        if (!dispatcherQueue.TryEnqueue(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }

                try
                {
                    completion.TrySetResult(ActivateOnUiThread(request));
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }))
        {
            throw new InvalidOperationException(
                "The UI dispatcher rejected an activation request.");
        }

        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
