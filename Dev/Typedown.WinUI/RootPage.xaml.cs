using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Typedown.Contracts.Editor;
using Typedown.Contracts.Localization;
using Typedown.WinUI.Editor;

namespace Typedown.WinUI;

public sealed partial class RootPage : Page
{
    private readonly IEditorDocumentSession documentSession;
    private readonly IStringLocalizer stringLocalizer;

    public RootPage(
        WinUIEditorHost editorHost,
        IEditorDocumentSession documentSession,
        IStringLocalizer stringLocalizer)
    {
        InitializeComponent();
        this.documentSession = documentSession
            ?? throw new ArgumentNullException(nameof(documentSession));
        this.stringLocalizer = stringLocalizer
            ?? throw new ArgumentNullException(nameof(stringLocalizer));
        EditorPresenter.Content = editorHost
            ?? throw new ArgumentNullException(nameof(editorHost));
    }

    private async void OnSaveInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        try
        {
            await documentSession.SaveAsync();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Saving the active document failed: {exception}");
        }
    }
}
