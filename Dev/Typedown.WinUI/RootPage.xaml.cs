using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Typedown.Automation;
using Typedown.Contracts.Editor;
using Typedown.Contracts.Localization;
using Typedown.Contracts.Platform;
using Typedown.Core.Utilities;
using Typedown.WinUI.Editor;

namespace Typedown.WinUI;

public sealed partial class RootPage : Page, IDisposable
{
    private readonly IEditorDocumentSession documentSession;
    private readonly IStringLocalizer stringLocalizer;
    private readonly IFilePickerService filePicker;
    private readonly IDialogService dialogService;
    private readonly IWindowContext windowContext;
    private EditorDocumentState state;
    private int isDisposed;

    public RootPage(
        WinUIEditorHost editorHost,
        IEditorDocumentSession documentSession,
        IStringLocalizer stringLocalizer,
        IFilePickerService filePicker,
        IDialogService dialogService,
        IWindowContext windowContext)
    {
        InitializeComponent();
        this.documentSession = documentSession
            ?? throw new ArgumentNullException(nameof(documentSession));
        this.stringLocalizer = stringLocalizer
            ?? throw new ArgumentNullException(nameof(stringLocalizer));
        this.filePicker = filePicker
            ?? throw new ArgumentNullException(nameof(filePicker));
        this.dialogService = dialogService
            ?? throw new ArgumentNullException(nameof(dialogService));
        this.windowContext = windowContext
            ?? throw new ArgumentNullException(nameof(windowContext));
        EditorPresenter.Content = editorHost
            ?? throw new ArgumentNullException(nameof(editorHost));

        state = documentSession.State;
        documentSession.StateChanged += OnDocumentStateChanged;
        stringLocalizer.LanguageChanged += OnLanguageChanged;
        ApplyLocalizedText();
        ApplyDocumentState(state);
    }

    private async void OnOpenInvoked(object sender, RoutedEventArgs args)
    {
        await OpenDocumentAsync();
    }

    private async void OnOpenAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await OpenDocumentAsync();
    }

    private async void OnSaveInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await SaveDocumentAsync();
    }

    private async void OnSaveMenuInvoked(object sender, RoutedEventArgs args)
    {
        await SaveDocumentAsync();
    }

    private async void OnCloseWindowInvoked(object sender, RoutedEventArgs args)
    {
        try
        {
            await windowContext.RequestCloseAsync();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Closing the active window failed: {exception}");
        }
    }

    private async Task OpenDocumentAsync()
    {
        try
        {
            var result = await filePicker.OpenFilesAsync(new OpenFilePickerRequest
            {
                AllowMultiple = false,
                SuggestedStartPath = state.FilePath is null
                    ? null
                    : Path.GetDirectoryName(state.FilePath),
                OwnerWindowId = windowContext.Id,
                FileTypes = new[]
                {
                    new FileTypeChoice
                    {
                        Description = stringLocalizer.GetString("Files"),
                        Patterns = FileTypeHelper.Markdown
                            .Concat(FileTypeHelper.PlainText)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(pattern => pattern, StringComparer.OrdinalIgnoreCase)
                            .ToArray(),
                    },
                },
            });
            if (result.Status == FilePickerStatus.Accepted
                && result.Paths.FirstOrDefault() is { } filePath)
            {
                await documentSession.OpenAsync(filePath);
            }
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception);
        }
    }

    private async Task SaveDocumentAsync()
    {
        try
        {
            await documentSession.SaveAsync();
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception);
        }
    }

    private async Task ShowErrorAsync(Exception exception)
    {
        System.Diagnostics.Debug.WriteLine(exception);
        try
        {
            await dialogService.ShowAsync(new DialogRequest
            {
                Title = stringLocalizer.GetString("Error"),
                Message = exception.Message,
                PrimaryButtonText = stringLocalizer.GetString("Ok"),
                OwnerWindowId = windowContext.Id,
            });
        }
        catch (Exception dialogException)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Displaying a shell error failed: {dialogException}");
        }
    }

    private void OnDocumentStateChanged(
        object? sender,
        EditorDocumentStateChangedEventArgs args)
    {
        DispatchShellUpdate(() => ApplyDocumentState(args.State));
    }

    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs args)
    {
        DispatchShellUpdate(() =>
        {
            ApplyLocalizedText();
            ApplyDocumentState(state);
        });
    }

    private void ApplyLocalizedText()
    {
        FileMenu.Title = stringLocalizer.GetString("File");
        OpenFileItem.Text = stringLocalizer.GetString("Open");
        SaveFileItem.Text = stringLocalizer.GetString("Save");
        CloseWindowItem.Text = stringLocalizer.GetString("Close");
    }

    private void ApplyDocumentState(EditorDocumentState current)
    {
        state = current;
        var fileName = current.FilePath is null
            ? stringLocalizer.GetString("Untitled")
            : Path.GetFileName(current.FilePath);
        var dirtyPrefix = current.IsDirty ? "*" : string.Empty;

        DocumentTitleText.Text = dirtyPrefix + fileName;
        windowContext.Title = current.FilePath is null && !current.IsDirty
            ? Brand.Name
            : $"{dirtyPrefix}{fileName} - {Brand.Name}";
        SaveFileItem.IsEnabled = current.FilePath is not null;
        WordCountText.Text = $"{current.WordCount} {stringLocalizer.GetString(
            current.WordCount == 1 ? "Word" : "Words")}";
        CharacterCountText.Text = $"{current.CharacterCount} {stringLocalizer.GetString(
            current.CharacterCount == 1 ? "Character" : "Characters")}";
    }

    private void DispatchShellUpdate(Action update)
    {
        if (Volatile.Read(ref isDisposed) != 0
            || windowContext.Dispatcher.IsShutdownStarted)
        {
            return;
        }

        if (windowContext.Dispatcher.HasThreadAccess)
        {
            update();
            return;
        }

        _ = DispatchShellUpdateAsync(update);
    }

    private async Task DispatchShellUpdateAsync(Action update)
    {
        try
        {
            await windowContext.Dispatcher.InvokeAsync(update);
        }
        catch (Exception exception) when (exception is ObjectDisposedException
            or InvalidOperationException
            or OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Skipping a shell update during window shutdown: {exception.Message}");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }
        documentSession.StateChanged -= OnDocumentStateChanged;
        stringLocalizer.LanguageChanged -= OnLanguageChanged;
    }
}
