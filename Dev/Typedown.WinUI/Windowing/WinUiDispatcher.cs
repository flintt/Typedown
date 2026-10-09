using Microsoft.UI.Dispatching;
using Typedown.Contracts.Platform;

namespace Typedown.WinUI.Windowing;

public sealed class WinUiDispatcher : IUiDispatcher, IDisposable
{
    private readonly DispatcherQueue dispatcherQueue;
    private int isDisposed;
    private int isShutdownStarted;

    public WinUiDispatcher(DispatcherQueue dispatcherQueue)
    {
        this.dispatcherQueue = dispatcherQueue
            ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        this.dispatcherQueue.ShutdownStarting += OnShutdownStarting;
    }

    public bool HasThreadAccess => dispatcherQueue.HasThreadAccess;

    public bool IsShutdownStarted =>
        Volatile.Read(ref isDisposed) != 0 || Volatile.Read(ref isShutdownStarted) != 0;

    public ValueTask InvokeAsync(
        Action callback,
        UiDispatchPriority priority = UiDispatchPriority.Normal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return InvokeAsync(
            _ =>
            {
                callback();
                return ValueTask.CompletedTask;
            },
            priority,
            cancellationToken);
    }

    public ValueTask<T> InvokeAsync<T>(
        Func<T> callback,
        UiDispatchPriority priority = UiDispatchPriority.Normal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return InvokeAsync(
            _ => ValueTask.FromResult(callback()),
            priority,
            cancellationToken);
    }

    public ValueTask InvokeAsync(
        Func<CancellationToken, ValueTask> callback,
        UiDispatchPriority priority = UiDispatchPriority.Normal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        cancellationToken.ThrowIfCancellationRequested();

        if (IsShutdownStarted)
        {
            return ValueTask.FromException(CreateShutdownException());
        }

        if (dispatcherQueue.HasThreadAccess)
        {
            try
            {
                return callback(cancellationToken);
            }
            catch (Exception exception)
            {
                return ValueTask.FromException(exception);
            }
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationRegistration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));

        if (!TryEnqueue(priority, RunCallback))
        {
            cancellationRegistration.Dispose();
            completion.TrySetException(CreateShutdownException());
        }

        return new ValueTask(completion.Task);

        void RunCallback()
        {
            _ = CompleteAsync();
        }

        async Task CompleteAsync()
        {
            try
            {
                if (!completion.Task.IsCompleted)
                {
                    await callback(cancellationToken);
                    completion.TrySetResult();
                }
            }
            catch (OperationCanceledException exception)
            {
                if (exception.CancellationToken.CanBeCanceled)
                {
                    completion.TrySetCanceled(exception.CancellationToken);
                }
                else
                {
                    completion.TrySetCanceled();
                }
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
            finally
            {
                cancellationRegistration.Dispose();
            }
        }
    }

    public ValueTask<T> InvokeAsync<T>(
        Func<CancellationToken, ValueTask<T>> callback,
        UiDispatchPriority priority = UiDispatchPriority.Normal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        cancellationToken.ThrowIfCancellationRequested();

        if (IsShutdownStarted)
        {
            return ValueTask.FromException<T>(CreateShutdownException());
        }

        if (dispatcherQueue.HasThreadAccess)
        {
            try
            {
                return callback(cancellationToken);
            }
            catch (Exception exception)
            {
                return ValueTask.FromException<T>(exception);
            }
        }

        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationRegistration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));

        if (!TryEnqueue(priority, RunCallback))
        {
            cancellationRegistration.Dispose();
            completion.TrySetException(CreateShutdownException());
        }

        return new ValueTask<T>(completion.Task);

        void RunCallback()
        {
            _ = CompleteAsync();
        }

        async Task CompleteAsync()
        {
            try
            {
                if (!completion.Task.IsCompleted)
                {
                    var result = await callback(cancellationToken);
                    completion.TrySetResult(result);
                }
            }
            catch (OperationCanceledException exception)
            {
                if (exception.CancellationToken.CanBeCanceled)
                {
                    completion.TrySetCanceled(exception.CancellationToken);
                }
                else
                {
                    completion.TrySetCanceled();
                }
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
            finally
            {
                cancellationRegistration.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        dispatcherQueue.ShutdownStarting -= OnShutdownStarting;
    }

    private bool TryEnqueue(UiDispatchPriority priority, DispatcherQueueHandler callback)
    {
        try
        {
            return !IsShutdownStarted
                && dispatcherQueue.TryEnqueue(MapPriority(priority), callback);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void OnShutdownStarting(
        DispatcherQueue sender,
        DispatcherQueueShutdownStartingEventArgs args)
    {
        Volatile.Write(ref isShutdownStarted, 1);
    }

    private static DispatcherQueuePriority MapPriority(UiDispatchPriority priority) =>
        priority switch
        {
            UiDispatchPriority.Low => DispatcherQueuePriority.Low,
            UiDispatchPriority.High => DispatcherQueuePriority.High,
            _ => DispatcherQueuePriority.Normal,
        };

    private static ObjectDisposedException CreateShutdownException() =>
        new(nameof(WinUiDispatcher), "The owning UI dispatcher is shutting down.");
}
