namespace Typedown.Contracts.Platform;

public enum FilePickerStatus
{
    Accepted,
    Cancelled,
}

public sealed record FileTypeChoice
{
    public required string Description { get; init; }

    public IReadOnlyList<string> Patterns { get; init; } = Array.Empty<string>();
}

public sealed record OpenFilePickerRequest
{
    public bool AllowMultiple { get; init; }

    public IReadOnlyList<FileTypeChoice> FileTypes { get; init; }
        = Array.Empty<FileTypeChoice>();

    public string? SuggestedStartPath { get; init; }

    public WindowId? OwnerWindowId { get; init; }
}

public sealed record SaveFilePickerRequest
{
    public string? SuggestedFileName { get; init; }

    public string? DefaultExtension { get; init; }

    public IReadOnlyList<FileTypeChoice> FileTypes { get; init; }
        = Array.Empty<FileTypeChoice>();

    public string? SuggestedStartPath { get; init; }

    public WindowId? OwnerWindowId { get; init; }
}

public sealed record FolderPickerRequest
{
    public string? SuggestedStartPath { get; init; }

    public WindowId? OwnerWindowId { get; init; }
}

public sealed record FilePickerResult
{
    public required FilePickerStatus Status { get; init; }

    public IReadOnlyList<string> Paths { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Opens host pickers and returns ordinary paths. A dismissed picker returns a
/// cancelled result; token cancellation interrupts the operation.
/// </summary>
public interface IFilePickerService
{
    ValueTask<FilePickerResult> OpenFilesAsync(
        OpenFilePickerRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<FilePickerResult> SaveFileAsync(
        SaveFilePickerRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<FilePickerResult> PickFolderAsync(
        FolderPickerRequest request,
        CancellationToken cancellationToken = default);
}
