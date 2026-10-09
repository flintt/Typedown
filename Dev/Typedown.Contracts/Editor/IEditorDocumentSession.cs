namespace Typedown.Contracts.Editor;

public sealed record EditorDocumentState(
    string? FilePath,
    string Text,
    bool IsDirty,
    int WordCount,
    int CharacterCount);

public sealed class EditorDocumentStateChangedEventArgs : EventArgs
{
    public EditorDocumentStateChangedEventArgs(EditorDocumentState state)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    public EditorDocumentState State { get; }
}

/// <summary>
/// Owns the active document behind one editor page. Implementations preserve
/// the document's byte format and synchronize with the page before saving.
/// </summary>
public interface IEditorDocumentSession : IDisposable
{
    EditorDocumentState State { get; }

    string? FilePath { get; }

    string Text { get; }

    bool IsDirty { get; }

    /// <summary>
    /// Publishes an immutable, internally consistent snapshot. The event may
    /// originate outside a window's UI thread; UI consumers must dispatch it.
    /// </summary>
    event EventHandler<EditorDocumentStateChangedEventArgs>? StateChanged;

    Task OpenAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<bool> FlushAsync(
        int timeoutMilliseconds = 2000,
        CancellationToken cancellationToken = default);

    Task<bool> SaveAsync(CancellationToken cancellationToken = default);
}
