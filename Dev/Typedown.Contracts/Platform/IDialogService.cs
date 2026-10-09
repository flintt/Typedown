namespace Typedown.Contracts.Platform;

public enum DialogDefaultButton
{
    Primary,
    Secondary,
    Cancel,
}

public enum DialogOutcome
{
    Primary,
    Secondary,
    Cancelled,
    Dismissed,
}

public sealed record DialogRequest
{
    public string? Title { get; init; }

    public required string Message { get; init; }

    public string PrimaryButtonText { get; init; } = "OK";

    public string? SecondaryButtonText { get; init; }

    public string? CancelButtonText { get; init; }

    public DialogDefaultButton DefaultButton { get; init; } = DialogDefaultButton.Primary;

    public WindowId? OwnerWindowId { get; init; }
}

public sealed record DialogResult(DialogOutcome Outcome);

/// <summary>
/// Shows a modal dialog for the owning window. Token cancellation interrupts
/// the operation; a user cancellation is reported through <see cref="DialogResult"/>.
/// </summary>
public interface IDialogService
{
    ValueTask<DialogResult> ShowAsync(
        DialogRequest request,
        CancellationToken cancellationToken = default);
}
