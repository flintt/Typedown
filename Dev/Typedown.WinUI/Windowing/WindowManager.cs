using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Typedown.Contracts.Platform;

namespace Typedown.WinUI.Windowing;

public sealed class WindowManager
{
    private readonly Dictionary<WindowId, WindowSession> sessions = new();
    private readonly object sessionsGate = new();
    private readonly DispatcherQueue uiDispatcherQueue;
    private readonly IServiceScopeFactory serviceScopeFactory;
    private readonly string defaultTitle;

    public WindowManager(
        IServiceScopeFactory serviceScopeFactory,
        string defaultTitle)
    {
        this.serviceScopeFactory = serviceScopeFactory
            ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
        this.defaultTitle = defaultTitle
            ?? throw new ArgumentNullException(nameof(defaultTitle));
        uiDispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException(
                "WindowManager must be created on the application's UI thread.");
    }

    public int Count
    {
        get
        {
            lock (sessionsGate)
            {
                return sessions.Count;
            }
        }
    }

    public IReadOnlyCollection<WindowSession> Sessions
    {
        get
        {
            lock (sessionsGate)
            {
                return sessions.Values.ToArray();
            }
        }
    }

    public WindowSession CreateWindow()
    {
        EnsureThreadAccess();

        var window = new Window();
        var id = new WindowId(Guid.NewGuid().ToString("N"));
        var serviceScope = serviceScopeFactory.CreateScope();
        WindowSession? session = null;

        try
        {
            session = new WindowSession(window, id, serviceScope);
            session.Closed += OnSessionClosed;
            lock (sessionsGate)
            {
                sessions.Add(id, session);
            }
            session.Context.Title = defaultTitle;
            window.Content = session.Services.GetRequiredService<RootPage>();
            window.Activate();
            return session;
        }
        catch
        {
            if (session is not null)
            {
                session.Closed -= OnSessionClosed;
                lock (sessionsGate)
                {
                    sessions.Remove(id);
                }
                session.AbortCreation();
            }
            else
            {
                serviceScope.Dispose();
            }

            window.Close();
            throw;
        }
    }

    public bool TryGetSession(WindowId id, out WindowSession? session)
    {
        EnsureThreadAccess();
        lock (sessionsGate)
        {
            return sessions.TryGetValue(id, out session);
        }
    }

    internal WindowAutomationSnapshot GetAutomationSnapshot()
    {
        lock (sessionsGate)
        {
            var active = sessions.Values
                .Select(session => session.Context)
                .FirstOrDefault(context => context.IsActive);
            return new WindowAutomationSnapshot(
                sessions.Count,
                active?.Id.Value);
        }
    }

    private void OnSessionClosed(object? sender, EventArgs args)
    {
        EnsureThreadAccess();
        if (sender is not WindowSession session)
        {
            return;
        }

        session.Closed -= OnSessionClosed;
        lock (sessionsGate)
        {
            sessions.Remove(session.Context.Id);
        }
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

internal sealed record WindowAutomationSnapshot(
    int WindowCount,
    string? ActiveWindowId);
