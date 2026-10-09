using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Typedown.Contracts.Platform;

namespace Typedown.WinUI.Windowing;

public sealed class WindowManager
{
    private readonly Dictionary<WindowId, WindowSession> sessions = new();
    private readonly DispatcherQueue uiDispatcherQueue;
    private readonly Func<IWindowContext, UIElement> contentFactory;
    private readonly string defaultTitle;

    public WindowManager(
        Func<IWindowContext, UIElement> contentFactory,
        string defaultTitle)
    {
        this.contentFactory = contentFactory
            ?? throw new ArgumentNullException(nameof(contentFactory));
        this.defaultTitle = defaultTitle
            ?? throw new ArgumentNullException(nameof(defaultTitle));
        uiDispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException(
                "WindowManager must be created on the application's UI thread.");
    }

    public int Count => sessions.Count;

    public IReadOnlyCollection<WindowSession> Sessions => sessions.Values;

    public WindowSession CreateWindow()
    {
        EnsureThreadAccess();

        var window = new Window();
        var id = new WindowId(Guid.NewGuid().ToString("N"));
        var session = new WindowSession(window, id);
        session.Closed += OnSessionClosed;
        sessions.Add(id, session);

        try
        {
            session.Context.Title = defaultTitle;
            window.Content = contentFactory(session.Context);
            window.Activate();
            return session;
        }
        catch
        {
            session.Closed -= OnSessionClosed;
            sessions.Remove(id);
            session.AbortCreation();
            window.Close();
            throw;
        }
    }

    public bool TryGetSession(WindowId id, out WindowSession? session)
    {
        EnsureThreadAccess();
        return sessions.TryGetValue(id, out session);
    }

    private void OnSessionClosed(object? sender, EventArgs args)
    {
        EnsureThreadAccess();
        if (sender is not WindowSession session)
        {
            return;
        }

        session.Closed -= OnSessionClosed;
        sessions.Remove(session.Context.Id);
    }

    private void EnsureThreadAccess()
    {
        if (!uiDispatcherQueue.HasThreadAccess)
        {
            throw new InvalidOperationException(
                "Windows must be created and tracked on the application's UI thread.");
        }
    }
}
