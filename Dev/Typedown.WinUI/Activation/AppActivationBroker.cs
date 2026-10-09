using System.Threading.Channels;
using Microsoft.Windows.AppLifecycle;

namespace Typedown.WinUI.Activation;

/// <summary>
/// Captures redirected activations immediately after process startup. The native
/// callback only writes to a channel, so application work never runs inside it.
/// </summary>
internal sealed class AppActivationBroker : IDisposable
{
    private readonly AppInstance appInstance;
    private readonly Channel<AppActivationArguments> activations =
        Channel.CreateUnbounded<AppActivationArguments>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = true,
            SingleWriter = false,
        });
    private int isDisposed;

    public AppActivationBroker(AppInstance appInstance)
    {
        this.appInstance = appInstance
            ?? throw new ArgumentNullException(nameof(appInstance));
        this.appInstance.Activated += OnAppInstanceActivated;
    }

    public IAsyncEnumerable<AppActivationArguments> ReadAllAsync(
        CancellationToken cancellationToken = default) =>
        activations.Reader.ReadAllAsync(cancellationToken);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        appInstance.Activated -= OnAppInstanceActivated;
        activations.Writer.TryComplete();
    }

    private void OnAppInstanceActivated(object? sender, AppActivationArguments args)
    {
        if (Volatile.Read(ref isDisposed) == 0)
        {
            activations.Writer.TryWrite(args);
        }
    }
}
