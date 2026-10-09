namespace Typedown.Contracts.Editor;

public enum EditorBridgeState
{
    NotStarted,
    Initializing,
    Navigating,
    Ready,
    Faulted,
    Disposed,
}

public sealed class EditorRawMessageReceivedEventArgs : EventArgs
{
    public EditorRawMessageReceivedEventArgs(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        Json = json;
    }

    public string Json { get; }
}

public sealed class EditorBridgeStateChangedEventArgs : EventArgs
{
    public EditorBridgeStateChangedEventArgs(
        EditorBridgeState previous,
        EditorBridgeState current,
        string? error = null)
    {
        Previous = previous;
        Current = current;
        Error = error;
    }

    public EditorBridgeState Previous { get; }

    public EditorBridgeState Current { get; }

    public string? Error { get; }
}

/// <summary>
/// Carries the existing editor protocol as opaque JSON. Protocol parsing and
/// document state belong to the window-scoped editor session, not the host UI.
/// </summary>
public interface IMarkdownEditorBridge : IDisposable
{
    EditorBridgeState State { get; }

    event EventHandler<EditorRawMessageReceivedEventArgs>? RawMessageReceived;

    event EventHandler<EditorBridgeStateChangedEventArgs>? StateChanged;

    bool TryPostJson(string json);

    void Focus();
}
