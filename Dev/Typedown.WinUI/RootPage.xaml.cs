using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Typedown.Contracts.Editor;
using Typedown.WinUI.Editor;

namespace Typedown.WinUI;

public sealed partial class RootPage : Page
{
    private readonly IEditorDocumentSession documentSession;

    public RootPage(
        WinUIEditorHost editorHost,
        IEditorDocumentSession documentSession)
    {
        InitializeComponent();
        this.documentSession = documentSession
            ?? throw new ArgumentNullException(nameof(documentSession));
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
