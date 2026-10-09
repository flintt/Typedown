using Microsoft.UI.Xaml;
using Typedown.Contracts.Platform;
using Typedown.WinUI.Windowing;

namespace Typedown.WinUI.Services;

internal sealed class WindowRegistration
{
    private Window? window;
    private IWindowContext? context;
    private WinUiDispatcher? dispatcher;

    public Window Window => window
        ?? throw new InvalidOperationException("The window scope has not been initialized.");

    public IWindowContext Context => context
        ?? throw new InvalidOperationException("The window scope has not been initialized.");

    public WinUiDispatcher Dispatcher => dispatcher
        ?? throw new InvalidOperationException("The window scope has not been initialized.");

    public void Initialize(
        Window window,
        IWindowContext context,
        WinUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dispatcher);

        if (this.window is not null)
        {
            throw new InvalidOperationException("The window scope is already initialized.");
        }

        this.window = window;
        this.context = context;
        this.dispatcher = dispatcher;
    }
}
