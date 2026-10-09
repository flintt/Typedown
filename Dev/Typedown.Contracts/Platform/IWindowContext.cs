namespace Typedown.Contracts.Platform;

/// <summary>
/// A stable identity assigned by Typedown for the lifetime of a window.
/// It is independent of host handles and may be used in logs and routing DTOs.
/// </summary>
public readonly record struct WindowId
{
    public WindowId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;
}

public enum WindowCloseOutcome
{
    Closed,
    Cancelled,
    AlreadyClosed,
}

public sealed class WindowActivationChangedEventArgs : EventArgs
{
    public WindowActivationChangedEventArgs(bool isActive)
    {
        IsActive = isActive;
    }

    public bool IsActive { get; }
}

/// <summary>
/// Exposes only the window state that application services need.
/// </summary>
public interface IWindowContext
{
    WindowId Id { get; }

    string Title { get; set; }

    bool IsActive { get; }

    bool IsClosed { get; }

    IUiDispatcher Dispatcher { get; }

    event EventHandler<WindowActivationChangedEventArgs>? ActivationChanged;

    ValueTask ActivateAsync(CancellationToken cancellationToken = default);

    ValueTask<WindowCloseOutcome> RequestCloseAsync(
        CancellationToken cancellationToken = default);
}
