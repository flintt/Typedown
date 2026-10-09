using Microsoft.Windows.AppLifecycle;
using Typedown.Contracts.Platform;
using Windows.ApplicationModel.Activation;

namespace Typedown.WinUI.Activation;

internal sealed class AppActivationNormalizer
{
    private readonly string newWindowArgument;

    public AppActivationNormalizer(string newWindowArgument)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newWindowArgument);
        this.newWindowArgument = newWindowArgument;
    }

    public AppActivationRequest NormalizeInitial(
        AppActivationArguments? activation,
        string? launchArguments,
        IReadOnlyList<string> processArguments)
    {
        ArgumentNullException.ThrowIfNull(processArguments);

        if (activation is not null)
        {
            return Normalize(
                activation,
                AppActivationSource.Initial,
                processArguments);
        }

        var parsedLaunchArguments = ParseArgumentString(launchArguments);
        if (parsedLaunchArguments.Count > 0)
        {
            return CreateRequest(
                AppActivationKind.Launch,
                AppActivationSource.Initial,
                commandLineArguments: parsedLaunchArguments);
        }

        var fallbackArguments = processArguments.ToArray();
        return CreateRequest(
            fallbackArguments.Length == 0
                ? AppActivationKind.Launch
                : AppActivationKind.CommandLine,
            AppActivationSource.Initial,
            commandLineArguments: fallbackArguments);
    }

    public AppActivationRequest Normalize(
        AppActivationArguments activation,
        AppActivationSource source,
        IReadOnlyList<string>? fallbackArguments = null)
    {
        ArgumentNullException.ThrowIfNull(activation);

        if (activation.Data is IFileActivatedEventArgs fileArguments)
        {
            var paths = fileArguments.Files
                .Select(item => item.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToArray();
            return CreateRequest(
                AppActivationKind.Files,
                source,
                filePaths: paths);
        }

        if (activation.Data is IProtocolActivatedEventArgs protocolArguments)
        {
            return CreateRequest(
                AppActivationKind.Protocol,
                source,
                protocolUri: protocolArguments.Uri?.AbsoluteUri);
        }

        if (activation.Data is ICommandLineActivatedEventArgs commandLineArguments)
        {
            return CreateRequest(
                AppActivationKind.CommandLine,
                source,
                commandLineArguments: ParseArgumentString(
                    commandLineArguments.Operation.Arguments));
        }

        if (activation.Data is ILaunchActivatedEventArgs launchArguments)
        {
            var arguments = ParseArgumentString(launchArguments.Arguments);
            if (arguments.Count == 0 && fallbackArguments is { Count: > 0 })
            {
                return CreateRequest(
                    AppActivationKind.CommandLine,
                    source,
                    commandLineArguments: fallbackArguments);
            }

            return CreateRequest(
                AppActivationKind.Launch,
                source,
                commandLineArguments: arguments);
        }

        var fallback = fallbackArguments?.ToArray() ?? Array.Empty<string>();
        return CreateRequest(
            fallback.Length == 0
                ? AppActivationKind.Launch
                : AppActivationKind.CommandLine,
            source,
            commandLineArguments: fallback);
    }

    private AppActivationRequest CreateRequest(
        AppActivationKind kind,
        AppActivationSource source,
        IReadOnlyList<string>? filePaths = null,
        IReadOnlyList<string>? commandLineArguments = null,
        string? protocolUri = null)
    {
        var arguments = commandLineArguments ?? Array.Empty<string>();
        var openInNewWindow = arguments.Any(argument =>
            string.Equals(
                argument,
                newWindowArgument,
                StringComparison.OrdinalIgnoreCase));
        var publicArguments = arguments
            .Where(argument => !string.Equals(
                argument,
                newWindowArgument,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return new AppActivationRequest
        {
            ActivationId = Guid.NewGuid().ToString("N"),
            Kind = kind,
            Source = source,
            FilePaths = filePaths?.ToArray() ?? Array.Empty<string>(),
            CommandLineArguments = publicArguments,
            ProtocolUri = protocolUri,
            OpenInNewWindow = openInNewWindow,
        };
    }

    private static IReadOnlyList<string> ParseArgumentString(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        var current = new System.Text.StringBuilder(arguments.Length);
        var inQuotes = false;
        var hasArgument = false;

        for (var index = 0; index < arguments.Length; index++)
        {
            var character = arguments[index];
            if (character == '\\')
            {
                var slashStart = index;
                while (index < arguments.Length && arguments[index] == '\\')
                {
                    index++;
                }

                var slashCount = index - slashStart;
                if (index < arguments.Length && arguments[index] == '"')
                {
                    current.Append('\\', slashCount / 2);
                    hasArgument = true;
                    if (slashCount % 2 == 0)
                    {
                        inQuotes = !inQuotes;
                    }
                    else
                    {
                        current.Append('"');
                    }
                }
                else
                {
                    current.Append('\\', slashCount);
                    hasArgument = true;
                    index--;
                }

                continue;
            }

            if (character == '"')
            {
                inQuotes = !inQuotes;
                hasArgument = true;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                AddCurrentArgument(result, current, ref hasArgument);
                continue;
            }

            current.Append(character);
            hasArgument = true;
        }

        AddCurrentArgument(result, current, ref hasArgument);
        return result;
    }

    private static void AddCurrentArgument(
        ICollection<string> result,
        System.Text.StringBuilder current,
        ref bool hasArgument)
    {
        if (!hasArgument)
        {
            return;
        }

        result.Add(current.ToString());
        current.Clear();
        hasArgument = false;
    }
}
