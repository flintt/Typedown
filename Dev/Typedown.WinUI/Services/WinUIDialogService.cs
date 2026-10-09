using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Typedown.Contracts.Platform;

namespace Typedown.WinUI.Services;

internal sealed class WinUIDialogService : IDialogService
{
    private readonly Window window;

    public WinUIDialogService(Window window)
    {
        this.window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public async ValueTask<DialogResult> ShowAsync(
        DialogRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var cancelledByButton = false;
        var dialog = new ContentDialog
        {
            Title = request.Title,
            Content = request.Message,
            PrimaryButtonText = request.PrimaryButtonText,
            SecondaryButtonText = request.SecondaryButtonText ?? string.Empty,
            CloseButtonText = request.CancelButtonText ?? string.Empty,
            DefaultButton = MapDefaultButton(request.DefaultButton),
            XamlRoot = ResolveXamlRoot()
        };
        dialog.CloseButtonClick += (_, _) => cancelledByButton = true;

        var result = await dialog.ShowAsync().AsTask(cancellationToken);
        var outcome = result switch
        {
            ContentDialogResult.Primary => DialogOutcome.Primary,
            ContentDialogResult.Secondary => DialogOutcome.Secondary,
            ContentDialogResult.None when cancelledByButton => DialogOutcome.Cancelled,
            ContentDialogResult.None => DialogOutcome.Dismissed,
            _ => DialogOutcome.Dismissed
        };
        return new DialogResult(outcome);
    }

    private XamlRoot ResolveXamlRoot()
    {
        if (window.Content is FrameworkElement { XamlRoot: not null } element)
        {
            return element.XamlRoot;
        }

        throw new InvalidOperationException(
            "The current WinUI window does not have a XamlRoot yet.");
    }

    private static ContentDialogButton MapDefaultButton(DialogDefaultButton defaultButton)
    {
        return defaultButton switch
        {
            DialogDefaultButton.Primary => ContentDialogButton.Primary,
            DialogDefaultButton.Secondary => ContentDialogButton.Secondary,
            DialogDefaultButton.Cancel => ContentDialogButton.Close,
            _ => ContentDialogButton.None
        };
    }
}
