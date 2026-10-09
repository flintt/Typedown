using Microsoft.UI.Xaml;
using Typedown.Contracts.Platform;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Typedown.WinUI.Services;

internal sealed class WinUIFilePickerService : IFilePickerService
{
    private readonly Window window;

    public WinUIFilePickerService(Window window)
    {
        this.window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public async ValueTask<FilePickerResult> OpenFilesAsync(
        OpenFilePickerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = ResolveSuggestedStartLocation(request.SuggestedStartPath)
        };
        AddOpenFileTypes(picker, request.FileTypes);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

        if (request.AllowMultiple)
        {
            var files = await picker.PickMultipleFilesAsync().AsTask(cancellationToken);
            return AcceptedOrCancelled(files.Select(file => file.Path));
        }

        var file = await picker.PickSingleFileAsync().AsTask(cancellationToken);
        return file is null
            ? Cancelled()
            : AcceptedOrCancelled(new[] { file.Path });
    }

    public async ValueTask<FilePickerResult> SaveFileAsync(
        SaveFilePickerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var picker = new FileSavePicker
        {
            SuggestedFileName = request.SuggestedFileName ?? string.Empty,
            SuggestedStartLocation = ResolveSuggestedStartLocation(request.SuggestedStartPath)
        };
        AddSaveFileTypes(picker, request.FileTypes);

        var defaultExtension = NormalizeExtension(request.DefaultExtension);
        if (defaultExtension is not null)
        {
            picker.DefaultFileExtension = defaultExtension;
        }

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        var file = await picker.PickSaveFileAsync().AsTask(cancellationToken);
        return file is null
            ? Cancelled()
            : AcceptedOrCancelled(new[] { file.Path });
    }

    public async ValueTask<FilePickerResult> PickFolderAsync(
        FolderPickerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var picker = new FolderPicker
        {
            SuggestedStartLocation = ResolveSuggestedStartLocation(request.SuggestedStartPath)
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

        var folder = await picker.PickSingleFolderAsync().AsTask(cancellationToken);
        return folder is null
            ? Cancelled()
            : AcceptedOrCancelled(new[] { folder.Path });
    }

    private static void AddOpenFileTypes(
        FileOpenPicker picker,
        IReadOnlyList<FileTypeChoice> choices)
    {
        var patterns = choices
            .SelectMany(choice => choice.Patterns)
            .Select(NormalizeOpenPattern)
            .Where(pattern => pattern is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var pattern in patterns.Length == 0 ? new[] { "*" } : patterns)
        {
            picker.FileTypeFilter.Add(pattern);
        }
    }

    private static void AddSaveFileTypes(
        FileSavePicker picker,
        IReadOnlyList<FileTypeChoice> choices)
    {
        var choiceNumber = 0;
        foreach (var choice in choices)
        {
            var extensions = choice.Patterns
                .Select(NormalizeExtension)
                .Where(extension => extension is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (extensions.Length == 0)
            {
                continue;
            }

            var description = string.IsNullOrWhiteSpace(choice.Description)
                ? "Files"
                : choice.Description.Trim();
            var uniqueDescription = description;
            while (picker.FileTypeChoices.ContainsKey(uniqueDescription))
            {
                choiceNumber++;
                uniqueDescription = $"{description} ({choiceNumber + 1})";
            }

            picker.FileTypeChoices.Add(uniqueDescription, extensions);
        }

        if (picker.FileTypeChoices.Count == 0)
        {
            picker.FileTypeChoices.Add("All files", new[] { "." });
        }
    }

    private static string? NormalizeOpenPattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }

        var value = pattern.Trim();
        if (value is "*" or "*.*")
        {
            return "*";
        }

        if (value.StartsWith("*.", StringComparison.Ordinal))
        {
            value = value[1..];
        }

        return value.StartsWith(".", StringComparison.Ordinal)
            ? value
            : $".{value}";
    }

    private static string? NormalizeExtension(string? extension)
    {
        var value = NormalizeOpenPattern(extension);
        return value == "*" ? null : value;
    }

    private static PickerLocationId ResolveSuggestedStartLocation(string? suggestedPath)
    {
        if (string.IsNullOrWhiteSpace(suggestedPath))
        {
            return PickerLocationId.DocumentsLibrary;
        }

        try
        {
            var fullPath = Path.GetFullPath(suggestedPath);
            if (IsInSpecialFolder(fullPath, Environment.SpecialFolder.MyPictures))
            {
                return PickerLocationId.PicturesLibrary;
            }

            if (IsInSpecialFolder(fullPath, Environment.SpecialFolder.MyMusic))
            {
                return PickerLocationId.MusicLibrary;
            }

            if (IsInSpecialFolder(fullPath, Environment.SpecialFolder.MyVideos))
            {
                return PickerLocationId.VideosLibrary;
            }
        }
        catch
        {
            // Fall back to Documents when the hint is not a valid absolute path.
        }

        return PickerLocationId.DocumentsLibrary;
    }

    private static bool IsInSpecialFolder(
        string path,
        Environment.SpecialFolder specialFolder)
    {
        var root = Environment.GetFolderPath(specialFolder);
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        var relative = Path.GetRelativePath(Path.GetFullPath(root), path);
        return relative == "."
            || (!relative.StartsWith("..", StringComparison.Ordinal)
                && !Path.IsPathRooted(relative));
    }

    private static FilePickerResult AcceptedOrCancelled(IEnumerable<string?> paths)
    {
        var acceptedPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();
        return acceptedPaths.Length == 0
            ? Cancelled()
            : new FilePickerResult
            {
                Status = FilePickerStatus.Accepted,
                Paths = acceptedPaths
            };
    }

    private static FilePickerResult Cancelled()
    {
        return new FilePickerResult
        {
            Status = FilePickerStatus.Cancelled,
            Paths = Array.Empty<string>()
        };
    }
}
