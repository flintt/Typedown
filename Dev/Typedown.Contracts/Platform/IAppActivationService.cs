namespace Typedown.Contracts.Platform;

public enum AppActivationKind
{
    Launch,
    Files,
    CommandLine,
    Protocol,
}

public enum AppActivationSource
{
    Initial,
    Redirected,
}

public sealed record AppActivationRequest
{
    public required string ActivationId { get; init; }

    public AppActivationKind Kind { get; init; }

    public AppActivationSource Source { get; init; }

    public IReadOnlyList<string> FilePaths { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> CommandLineArguments { get; init; }
        = Array.Empty<string>();

    public string? ProtocolUri { get; init; }

    public WindowId? PreferredWindowId { get; init; }

    public bool OpenInNewWindow { get; init; }
}

public enum AppActivationStatus
{
    Handled,
    Redirected,
    Rejected,
}

public sealed record AppActivationResult
{
    public required AppActivationStatus Status { get; init; }

    public WindowId? WindowId { get; init; }

    public string? ErrorCode { get; init; }
}

/// <summary>
/// Normalizes initial and forwarded activation into platform-neutral requests.
/// Each activation ID is stable for a single request and may be used for
/// deduplication.
/// </summary>
public interface IAppActivationService
{
    ValueTask<AppActivationResult> ActivateAsync(
        AppActivationRequest request,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<AppActivationRequest> ListenAsync(
        CancellationToken cancellationToken = default);
}
