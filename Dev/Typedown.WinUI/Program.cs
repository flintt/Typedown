using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Typedown.WinUI.Activation;

namespace Typedown.WinUI;

internal static class Program
{
    internal static readonly string MainInstanceKey =
        $"{typeof(Program).Assembly.GetName().Name}.Main";
    internal const string NewWindowArgument = "--new-window";
    private static readonly TimeSpan RedirectActivationTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RedirectCancellationTimeout = TimeSpan.FromSeconds(1);

    [STAThread]
    private static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var currentInstance = AppInstance.GetCurrent();
        var initialActivation = currentInstance.GetActivatedEventArgs();
        var activationBroker = new AppActivationBroker(currentInstance);
        var mainInstance = AppInstance.FindOrRegisterForKey(MainInstanceKey);

        if (!mainInstance.IsCurrent)
        {
            activationBroker.Dispose();
            RedirectSecondaryActivation(mainInstance, initialActivation);
            return;
        }

        Application.Start(_ =>
        {
            var dispatcher = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(dispatcher));
            new App(initialActivation, args, activationBroker);
        });
    }

    private static void RedirectSecondaryActivation(
        AppInstance mainInstance,
        AppActivationArguments? initialActivation)
    {
        if (initialActivation is null)
        {
            Debug.WriteLine(
                "The application could not redirect this activation because its payload was unavailable.");
            return;
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellation = new CancellationTokenSource();
        var disposeCancellation = true;
        Exception? redirectError = null;

        ThreadPool.QueueUserWorkItem(async _ =>
        {
            try
            {
                await mainInstance.RedirectActivationToAsync(initialActivation)
                    .AsTask(cancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // The bounded startup wait requested cancellation.
            }
            catch (Exception exception)
            {
                redirectError = exception;
            }
            finally
            {
                completion.TrySetResult();
            }
        });

        try
        {
            if (!completion.Task.Wait(RedirectActivationTimeout))
            {
                Debug.WriteLine(
                    "Application activation redirection timed out; cancelling the pending redirect.");

                try
                {
                    cancellation.Cancel();
                }
                catch (Exception exception)
                {
                    Debug.WriteLine(
                        $"The application could not cancel activation redirection: {exception}");
                    disposeCancellation = false;
                    return;
                }

                if (!completion.Task.Wait(RedirectCancellationTimeout))
                {
                    Debug.WriteLine(
                        "Application activation redirection did not stop within the cancellation grace period.");
                    disposeCancellation = false;
                    return;
                }
            }

            if (redirectError is not null)
            {
                Debug.WriteLine($"Application activation redirection failed: {redirectError}");
            }
        }
        finally
        {
            if (disposeCancellation)
            {
                cancellation.Dispose();
            }
        }
    }
}
