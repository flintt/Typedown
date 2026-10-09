namespace Typedown.Contracts.Editor;

/// <summary>
/// Owns the active document behind one editor page. Implementations preserve
/// the document's byte format and synchronize with the page before saving.
/// </summary>
public interface IEditorDocumentSession : IDisposable
{
    string? FilePath { get; }

    string Text { get; }

    bool IsDirty { get; }

    Task OpenAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<bool> FlushAsync(
        int timeoutMilliseconds = 2000,
        CancellationToken cancellationToken = default);

    Task<bool> SaveAsync(CancellationToken cancellationToken = default);
}
