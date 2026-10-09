using Microsoft.UI.Xaml;
using Typedown.Contracts.Platform;

namespace Typedown.WinUI.Windowing;

public sealed class WindowContext : IWindowContext
{
    private readonly Window window;
    private string title;
    private int isActive;
    private int isClosed;

    internal WindowContext(Window window, WindowId id, WinUiDispatcher dispatcher)
    {
        this.window = window ?? throw new ArgumentNullException(nameof(window));
        Id = id;
        Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        title = window.Title;
        window.Activated += OnWindowActivated;
    }

    public WindowId Id { get; }

    public string Title
    {
        get => title;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            EnsureThreadAccess();
            ThrowIfClosed();
            window.Title = value;
            title = value;
        }
    }

    public bool IsActive => Volatile.Read(ref isActive) != 0;

    public bool IsClosed => Volatile.Read(ref isClosed) != 0;

    public IUiDispatcher Dispatcher { get; }

    public event EventHandler<WindowActivationChangedEventArgs>? ActivationChanged;

    public ValueTask ActivateAsync(CancellationToken cancellationToken = default) =>
        Dispatcher.InvokeAsync(
            () =>
            {
                ThrowIfClosed();
                window.Activate();
            },
            cancellationToken: cancellationToken);

    public ValueTask<WindowCloseOutcome> RequestCloseAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsClosed)
        {
            return ValueTask.FromResult(WindowCloseOutcome.AlreadyClosed);
        }

        return Dispatcher.InvokeAsync(
            () =>
            {
                if (IsClosed)
                {
                    return WindowCloseOutcome.AlreadyClosed;
                }

                window.Close();
                return IsClosed
                    ? WindowCloseOutcome.Closed
                    : WindowCloseOutcome.Cancelled;
            },
            cancellationToken: cancellationToken);
    }

    internal void MarkClosed()
    {
        if (Interlocked.Exchange(ref isClosed, 1) != 0)
        {
            return;
        }

        window.Activated -= OnWindowActivated;
        var wasActive = Interlocked.Exchange(ref isActive, 0) != 0;
        if (wasActive)
        {
            ActivationChanged?.Invoke(
                this,
                new WindowActivationChangedEventArgs(isActive: false));
        }
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (IsClosed)
        {
            return;
        }

        var active = args.WindowActivationState != WindowActivationState.Deactivated;
        var oldValue = Interlocked.Exchange(ref isActive, active ? 1 : 0) != 0;
        if (oldValue != active)
        {
            ActivationChanged?.Invoke(this, new WindowActivationChangedEventArgs(active));
        }
    }

    private void EnsureThreadAccess()
    {
        if (!Dispatcher.HasThreadAccess)
        {
            throw new InvalidOperationException(
                "Synchronous window properties must be changed on the owning UI thread.");
        }
    }

    private void ThrowIfClosed()
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
    }
}
