using Microsoft.UI.Xaml.Controls;
using Typedown.WinUI.Editor;

namespace Typedown.WinUI;

public sealed partial class RootPage : Page
{
    public RootPage(WinUIEditorHost editorHost)
    {
        InitializeComponent();
        EditorPresenter.Content = editorHost
            ?? throw new ArgumentNullException(nameof(editorHost));
    }
}
