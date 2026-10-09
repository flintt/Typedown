#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Typedown.Contracts.Editor;
using Typedown.Contracts.Platform;
using Typedown.Core.Services;
using Typedown.Core.Utilities;

namespace Typedown.Core.Editor;

/// <summary>
/// Keeps one window's active Markdown document and speaks the existing Muya
/// wire protocol over a platform-neutral bridge.
/// </summary>
public sealed class EditorDocumentSession : IEditorDocumentSession
{
    private static readonly JsonSerializerSettings ProtocolJson = new()
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new CamelCaseNamingStrategy(
                processDictionaryKeys: true,
                overrideSpecifiedNames: true),
        },
        MaxDepth = 256,
    };

    private static readonly IReadOnlyDictionary<string, string> EnglishEditorStrings =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["InputFootnoteDefine"] = "Input Footnote Definition...",
            ["InputYAMLFrontMatter"] = "Input YAML Front Matter...",
            ["InputMathFormula"] = "Input Math...",
            ["InputLanguageIdentifier"] = "Input Language Identifier...",
            ["ClickToAddAnImage"] = "Click to add an image",
            ["LoadImageFail"] = "Failed to Load Image",
            ["Footnote"] = "Footnotes",
            ["FirstEditWarning"] = "Editing this document in visual mode may rewrite some of its raw HTML. To keep it exactly as it is, edit it in source mode.",
            ["PlantUmlOff"] = "PlantUML diagrams are off (drawing sends the source to plantuml.com). Turn them on in Settings > Editor.",
        };

    private readonly IMarkdownEditorBridge bridge;
    private readonly JsonSettingsStore settings;
    private readonly IAppDataPathProvider paths;
    private readonly object stateLock = new();
    private readonly SemaphoreSlim documentGate = new(1, 1);
    private readonly Dictionary<string, string> previousMessages =
        new(StringComparer.Ordinal);
    private readonly Dictionary<int, TaskCompletionSource<bool>> flushWaiters = new();

    private TextFileFormat fileFormat = TextFileFormat.Default;
    private string? filePath;
    private string text = string.Empty;
    private bool isDirty;
    private bool pageHasSettings;
    private bool userEdited;
    private int loadId;
    private int flushToken;
    private int isDisposed;

    public EditorDocumentSession(
        IMarkdownEditorBridge bridge,
        JsonSettingsStore settings,
        IAppDataPathProvider paths)
    {
        this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        bridge.RawMessageReceived += OnRawMessageReceived;
        bridge.StateChanged += OnBridgeStateChanged;
    }

    public string? FilePath
    {
        get { lock (stateLock) return filePath; }
    }

    public string Text
    {
        get { lock (stateLock) return text; }
    }

    public bool IsDirty
    {
        get { lock (stateLock) return isDirty; }
    }

    public async Task OpenAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref isDisposed) != 0,
            this);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The Markdown document does not exist.", fullPath);
        }

        var loaded = await TextFileFormat.ReadAsync(fullPath);
        cancellationToken.ThrowIfCancellationRequested();

        await documentGate.WaitAsync(cancellationToken);
        try
        {
            int? pageLoadId = null;
            lock (stateLock)
            {
                this.filePath = fullPath;
                fileFormat = loaded.Format;
                text = loaded.Text;
                isDirty = false;
                userEdited = false;
                if (pageHasSettings && bridge.State == EditorBridgeState.Ready)
                {
                    pageLoadId = ++loadId;
                }
            }

            if (pageLoadId is { } currentLoadId)
            {
                PostMessage("LoadFile", new
                {
                    text = loaded.Text,
                    basePath = Path.GetDirectoryName(fullPath) ?? string.Empty,
                    cursor = (object?)null,
                    scrollTop = (double?)null,
                    loadId = currentLoadId,
                });
            }
        }
        finally
        {
            documentGate.Release();
        }
    }

    public async Task<bool> FlushAsync(
        int timeoutMilliseconds = 2000,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref isDisposed) != 0,
            this);
        if (timeoutMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        }

        TaskCompletionSource<bool> waiter;
        int token;
        lock (stateLock)
        {
            if (!pageHasSettings || bridge.State != EditorBridgeState.Ready)
            {
                return false;
            }
            token = ++flushToken;
            waiter = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            flushWaiters[token] = waiter;
        }

        try
        {
            if (!PostMessage("FlushContent", new { token }))
            {
                return false;
            }

            var timeout = Task.Delay(timeoutMilliseconds, cancellationToken);
            var completed = await Task.WhenAny(waiter.Task, timeout);
            if (completed == waiter.Task)
            {
                return await waiter.Task;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
        finally
        {
            lock (stateLock) flushWaiters.Remove(token);
        }
    }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref isDisposed) != 0,
            this);

        await documentGate.WaitAsync(cancellationToken);
        try
        {
            string? path;
            lock (stateLock) path = filePath;
            if (path is null)
            {
                return false;
            }

            _ = await FlushAsync(cancellationToken: cancellationToken);

            string currentText;
            TextFileFormat currentFormat;
            bool hasUserEdit;
            lock (stateLock)
            {
                if (!string.Equals(filePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                currentText = text;
                currentFormat = fileFormat;
                hasUserEdit = userEdited;
            }

            if (currentFormat.LossyDecode)
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(currentText)
                && File.Exists(path)
                && new FileInfo(path).Length > 0
                && !hasUserEdit)
            {
                return false;
            }

            await SafeFile.WriteAllBytesAtomicAsync(
                path,
                currentFormat.GetBytes(currentText));

            lock (stateLock)
            {
                if (!string.Equals(filePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                if (!string.Equals(text, currentText, StringComparison.Ordinal))
                {
                    isDirty = true;
                    return false;
                }
                isDirty = false;
                userEdited = false;
                return true;
            }
        }
        finally
        {
            documentGate.Release();
        }
    }

    private void OnRawMessageReceived(
        object? sender,
        EditorRawMessageReceivedEventArgs args)
    {
        IncomingMessage? message;
        try
        {
            message = JsonConvert.DeserializeObject<IncomingMessage>(
                args.Json,
                ProtocolJson);
        }
        catch
        {
            return;
        }
        if (message is null || string.IsNullOrWhiteSpace(message.Type))
        {
            return;
        }

        if (string.Equals(message.Type, "invoke", StringComparison.Ordinal))
        {
            HandleInvocation(message);
            return;
        }

        try
        {
            var payload = message.Type switch
            {
                "message" => message.Args,
                "diffmsg" => ExpandDiff(message),
                _ => null,
            };
            if (payload is not null)
            {
                HandlePageEvent(message.Name, payload);
            }
        }
        catch
        {
            // A malformed or out-of-baseline page event cannot alter the document.
        }
    }

    private void HandleInvocation(IncomingMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Id))
        {
            return;
        }

        try
        {
            var result = message.Name switch
            {
                "GetSettings" => CreateSettingsSnapshot(),
                "GetCurrentTheme" => CreateThemeSnapshot(),
                "GetStringResources" => CreateStringResources(message.Args),
                "ContentLoaded" => FocusEditor(),
                "UnhandledException" => JValue.CreateNull(),
                _ => throw new InvalidOperationException(
                    $"function [{message.Name}] does not exist"),
            };
            PostInvocationReply(message.Id, code: 0, data: result, error: null);
        }
        catch (Exception exception)
        {
            PostInvocationReply(
                message.Id,
                code: 1,
                data: null,
                error: exception.Message);
        }
    }

    private JToken FocusEditor()
    {
        bridge.Focus();
        return JValue.CreateNull();
    }

    private JToken ExpandDiff(IncomingMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Name))
        {
            throw new InvalidOperationException("A diff message has no name.");
        }
        var payload = message.Args?.Type == JTokenType.String
            ? message.Args.Value<string>() ?? string.Empty
            : message.Args?.ToString(Formatting.None) ?? string.Empty;

        if (message.Diff)
        {
            if (!previousMessages.TryGetValue(message.Name, out var baseline)
                || message.Start < 0
                || message.End < message.Start
                || message.End > baseline.Length)
            {
                throw new InvalidOperationException(
                    $"'{message.Name}' changed from a text this page did not send");
            }
            payload = baseline[..message.Start] + payload + baseline[message.End..];
        }

        previousMessages[message.Name] = payload;
        return JToken.Parse(payload);
    }

    private void HandlePageEvent(string? name, JToken payload)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var eventLoadId = payload.Value<int?>("loadId");
        lock (stateLock)
        {
            if (eventLoadId.HasValue && eventLoadId.Value != loadId)
            {
                return;
            }

            switch (name)
            {
                case "MarkdownChange":
                    if (payload.Value<string>("text") is { } changedText)
                    {
                        text = changedText;
                        isDirty = true;
                        userEdited = true;
                    }
                    break;

                case "FileLoaded":
                    if (payload.Value<string>("text") is { } loadedText)
                    {
                        text = loadedText;
                    }
                    break;

                case "ContentFlushed":
                    if (payload.Value<string>("text") is { } flushedText
                        && !string.Equals(text, flushedText, StringComparison.Ordinal))
                    {
                        text = flushedText;
                        isDirty = true;
                        userEdited = true;
                    }
                    var token = payload.Value<int?>("token") ?? 0;
                    if (flushWaiters.TryGetValue(token, out var waiter))
                    {
                        waiter.TrySetResult(true);
                    }
                    break;
            }
        }
    }

    private JObject CreateSettingsSnapshot()
    {
        string currentText;
        string basePath;
        int currentLoadId;
        lock (stateLock)
        {
            pageHasSettings = true;
            currentText = text;
            basePath = filePath is null
                ? string.Empty
                : Path.GetDirectoryName(filePath) ?? string.Empty;
            currentLoadId = ++loadId;
        }

        return new JObject
        {
            ["focusMode"] = settings.Get("FocusMode", false),
            ["typewriter"] = settings.Get("Typewriter", false),
            ["sourceCode"] = settings.Get("SourceCode", false),
            ["fontSize"] = settings.Get("FontSize", 16d),
            ["lineHeight"] = settings.Get("LineHeight", 1.6d),
            ["autoPairBracket"] = settings.Get("AutoPairBracket", true),
            ["autoPairQuote"] = settings.Get("AutoPairQuote", true),
            ["trimUnnecessaryCodeBlockEmptyLines"] = settings.Get(
                "TrimUnnecessaryCodeBlockEmptyLines",
                false),
            ["preferLooseListItem"] = settings.Get("PreferLooseListItem", true),
            ["listIndentation"] = settings.Get("ListIndentation", "1"),
            ["tableAlignColumns"] = settings.Get("TableAlignColumns", true),
            ["showParagraphMarker"] = settings.Get("ShowParagraphMarker", true),
            ["readOnly"] = settings.Get("ReadOnly", false),
            ["customCss"] = settings.Get("CustomCss", string.Empty),
            ["themeCss"] = ReadThemeCss(),
            ["editionCss"] = string.Empty,
            ["spellcheckEnabled"] = settings.Get("SpellcheckEnabled", false),
            ["autoPairMarkdownSyntax"] = settings.Get("AutoPairMarkdownSyntax", true),
            ["renderPlantUml"] = settings.Get("RenderPlantUml", false),
            ["plantUmlServer"] = settings.Get("PlantUmlServer", string.Empty),
            ["vimMode"] = settings.Get("VimMode", false),
            ["editorAreaWidth"] = settings.Get("EditorAreaWidth", "1200px"),
            ["fontFamily"] = settings.Get("FontFamily", string.Empty),
            ["textDirection"] = settings.Get("TextDirection", "auto"),
            ["tabSize"] = settings.Get("TabSize", 4),
            ["highlightAutomationChanges"] = settings.Get(
                "HighlightAutomationChanges",
                true),
            ["markdown"] = currentText,
            ["basePath"] = basePath,
            ["cursor"] = JValue.CreateNull(),
            ["scrollTop"] = JValue.CreateNull(),
            ["loadId"] = currentLoadId,
        };
    }

    private JObject CreateThemeSnapshot()
    {
        var configuredTheme = settings.Get("AppTheme", 0);
        var theme = configuredTheme switch
        {
            2 => "Dark",
            3 => "Black",
            _ => "Light",
        };
        var background = theme switch
        {
            "Black" => 0,
            "Dark" => 40,
            _ => 249,
        };
        return new JObject
        {
            ["theme"] = theme,
            ["accentColor"] = new JObject
            {
                ["r"] = 0,
                ["g"] = 120,
                ["b"] = 215,
                ["a"] = 1,
            },
            ["background"] = new JObject
            {
                ["R"] = background,
                ["G"] = background,
                ["B"] = background,
                ["A"] = 1,
            },
        };
    }

    private static JObject CreateStringResources(JToken? args)
    {
        var result = new JObject();
        foreach (var name in args?["names"]?.Values<string>() ?? Enumerable.Empty<string>())
        {
            if (name is null)
            {
                continue;
            }
            result[name] = EnglishEditorStrings.TryGetValue(name, out var value)
                ? value
                : name;
        }
        return result;
    }

    private string ReadThemeCss()
    {
        var id = settings.Get("CustomTheme", string.Empty);
        if (string.IsNullOrWhiteSpace(id)
            || !string.Equals(Path.GetFileName(id), id, StringComparison.Ordinal))
        {
            return string.Empty;
        }
        var fileName = id.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
            ? id
            : id + ".css";
        foreach (var candidate in new[]
        {
            Path.Combine(paths.ThemesDirectory, fileName),
            Path.Combine(AppContext.BaseDirectory, "Resources", "Themes", fileName),
        })
        {
            try
            {
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }
            }
            catch
            {
            }
        }
        return string.Empty;
    }

    private void PostInvocationReply(
        string id,
        int code,
        JToken? data,
        string? error)
    {
        var reply = new JObject { ["code"] = code };
        if (code == 0)
        {
            reply["data"] = data ?? JValue.CreateNull();
        }
        else
        {
            reply["msg"] = error ?? "The editor request failed.";
        }
        bridge.TryPostJson(new JObject
        {
            ["name"] = id,
            ["args"] = reply,
        }.ToString(Formatting.None));
    }

    private bool PostMessage(string name, object args) =>
        bridge.TryPostJson(JsonConvert.SerializeObject(
            new { name, args },
            ProtocolJson));

    private void OnBridgeStateChanged(
        object? sender,
        EditorBridgeStateChangedEventArgs args)
    {
        if (args.Current is not (EditorBridgeState.Navigating
            or EditorBridgeState.Faulted
            or EditorBridgeState.Disposed))
        {
            return;
        }

        List<TaskCompletionSource<bool>> pending;
        lock (stateLock)
        {
            pageHasSettings = false;
            previousMessages.Clear();
            pending = flushWaiters.Values.ToList();
        }
        foreach (var waiter in pending)
        {
            waiter.TrySetResult(false);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }
        bridge.RawMessageReceived -= OnRawMessageReceived;
        bridge.StateChanged -= OnBridgeStateChanged;

        List<TaskCompletionSource<bool>> pending;
        lock (stateLock)
        {
            pending = flushWaiters.Values.ToList();
            flushWaiters.Clear();
            previousMessages.Clear();
        }
        foreach (var waiter in pending)
        {
            waiter.TrySetResult(false);
        }
        documentGate.Dispose();
    }

    private sealed class IncomingMessage
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public JToken? Args { get; set; }
        public string? Type { get; set; }
        public bool Diff { get; set; }
        public int Start { get; set; }
        public int End { get; set; }
    }
}
