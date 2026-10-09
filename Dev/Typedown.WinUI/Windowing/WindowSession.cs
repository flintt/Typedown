using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Typedown.Contracts.Platform;
using Typedown.WinUI.Services;

namespace Typedown.WinUI.Windowing;

public sealed class WindowSession
{
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly CancellationToken lifetimeToken;
    private readonly IServiceScope serviceScope;
    private int isCompleted;

    internal WindowSession(Window window, WindowId id, IServiceScope serviceScope)
    {
        Window = window ?? throw new ArgumentNullException(nameof(window));
        this.serviceScope = serviceScope
            ?? throw new ArgumentNullException(nameof(serviceScope));
        lifetimeToken = lifetimeCancellation.Token;
        Dispatcher = new WinUiDispatcher(window.DispatcherQueue);
        Context = new WindowContext(window, id, Dispatcher);
        Services.GetRequiredService<WindowRegistration>()
            .Initialize(window, Context, Dispatcher);
        Window.Closed += OnWindowClosed;
    }

    public Window Window { get; }

    public WinUiDispatcher Dispatcher { get; }

    public WindowContext Context { get; }

    public IServiceProvider Services
    {
        get => serviceScope.ServiceProvider;
    }

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

        try
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            try
            {
                serviceScope.Dispose();
            }
            finally
            {
                Dispatcher.Dispose();
                lifetimeCancellation.Dispose();
            }
        }
    }
}
