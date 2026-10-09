using Microsoft.UI.Xaml;
using Typedown.Contracts.Platform;

namespace Typedown.WinUI.Windowing;

public sealed class WindowSession
{
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly CancellationToken lifetimeToken;
    private int isCompleted;

    internal WindowSession(Window window, WindowId id)
    {
        Window = window ?? throw new ArgumentNullException(nameof(window));
        lifetimeToken = lifetimeCancellation.Token;
        Dispatcher = new WinUiDispatcher(window.DispatcherQueue);
        Context = new WindowContext(window, id, Dispatcher);
        Window.Closed += OnWindowClosed;
    }

    public Window Window { get; }

    public WinUiDispatcher Dispatcher { get; }

    public WindowContext Context { get; }

    public CancellationToken LifetimeToken => lifetimeToken;

    public event EventHandler? Closed;

    internal void AbortCreation()
    {
        Complete();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (!args.Handled)
        {
            Complete();
        }
    }

    private void Complete()
    {
        if (Interlocked.Exchange(ref isCompleted, 1) != 0)
        {
            return;
        }

        Window.Closed -= OnWindowClosed;
        Context.MarkClosed();
        lifetimeCancellation.Cancel();
        Dispatcher.Dispose();

        try
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            lifetimeCancellation.Dispose();
        }
    }
}
