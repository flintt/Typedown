namespace Typedown.Contracts.Platform;

public enum UiDispatchPriority
{
    Low,
    Normal,
    High,
}

/// <summary>
/// Schedules work on the user-interface thread owned by one window.
/// </summary>
public interface IUiDispatcher
{
    bool HasThreadAccess { get; }

    bool IsShutdownStarted { get; }

    ValueTask InvokeAsync(
        Action callback,
        UiDispatchPriority priority = UiDispatchPriority.Normal,
        CancellationToken cancellationToken = default);

    ValueTask<T> InvokeAsync<T>(
        Func<T> callback,
        UiDispatchPriority priority = UiDispatchPriority.Normal,
        CancellationToken cancellationToken = default);

    ValueTask InvokeAsync(
        Func<CancellationToken, ValueTask> callback,
        UiDispatchPriority priority = UiDispatchPriority.Normal,
        CancellationToken cancellationToken = default);

    ValueTask<T> InvokeAsync<T>(
        Func<CancellationToken, ValueTask<T>> callback,
        UiDispatchPriority priority = UiDispatchPriority.Normal,
        CancellationToken cancellationToken = default);
}
