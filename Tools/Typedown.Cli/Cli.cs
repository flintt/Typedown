using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Typedown.Automation;

namespace Typedown.Cli
{
    /// <summary>
    /// typedownctl. Every command connects, initializes with only the scopes it needs, makes one call and prints the
    /// result: with --json exactly one JSON value on stdout (the result, or {"error": ...}), otherwise a short text for
    /// people. The exit code says what kind of failure it was; the JSON error's code and data.kind say exactly which.
    /// </summary>
    public sealed class Cli
    {
        public const int Ok = 0, Usage = 2, NotRunning = 3, Incompatible = 4, NotFound = 5, Conflict = 6, NotReady = 7, SaveFailed = 8, Other = 9;

        public const string Help = @"typedownctl - control a running Typedown through its local automation API

usage: typedownctl [--json] [--endpoint NAME] [--client-id UUID] <command> [options]

  status                                  application, version and windows
  windows                                 open windows
  documents [--window ID]                 open documents (no text)
  get <documentId> [--latest] [--text] [--headings]
                                          metadata; --latest flushes the editor first (default: snapshot)
  open <path> [--window ID] [--reveal]    open a file (an open one is returned as is)
  create [--text-file F | --stdin] [--window ID] [--reveal] [--allow-unknown-normalization]
  replace <documentId> --base-revision N (--text-file F | --stdin) [--save] [--reveal] [--lf]
          [--allow-unknown-normalization] [--client-operation-id S]
  replace-text <documentId> --base-revision N --find S --replacement S --expected-count N [--save] [--reveal]
          [--allow-unknown-normalization] [--client-operation-id S]
  save <documentId> [--base-revision N]
  settings describe | settings get [key ...] | settings set <key> <json value> --base-revision N
                                          external settings (appearance.theme, editor.fontSize, ...)

Document text uses \n line endings; text with \r is refused unless --lf converts it on the way in.
Exit codes: 0 ok, 2 usage, 3 not running or not reachable, 4 version or scope, 5 window/document not found,
6 revision or match-count conflict, 7 editor not ready, 8 save failed, 9 other.
";

        private readonly TextReader stdin;
        private readonly TextWriter stdout;
        private readonly TextWriter stderr;
        private readonly Func<string, CancellationToken, Task<Stream>> connect;

        public Cli(TextReader stdin, TextWriter stdout, TextWriter stderr, Func<string, CancellationToken, Task<Stream>>? connect = null)
        {
            this.stdin = stdin;
            this.stdout = stdout;
            this.stderr = stderr;
            this.connect = connect ?? ConnectPipeAsync;
        }

        private sealed class UsageException : Exception
        {
            public UsageException(string message) : base(message) { }
        }

        private sealed class Args
        {
            public readonly List<string> Positional = new();
            public readonly Dictionary<string, string?> Options = new();
            private readonly HashSet<string> used = new();

            public bool Flag(string name) { used.Add(name); return Options.ContainsKey(name); }
            public string? Value(string name) { used.Add(name); return Options.TryGetValue(name, out var v) ? v ?? throw new UsageException($"--{name} needs a value") : null; }
            public string Required(string name) => Value(name) ?? throw new UsageException($"--{name} is required");
            public long RequiredLong(string name) => long.TryParse(Required(name), out var v) && v >= 0 ? v : throw new UsageException($"--{name} must be a non-negative integer");
            public long? OptionalLong(string name) => Value(name) is string s ? (long.TryParse(s, out var v) && v >= 0 ? v : throw new UsageException($"--{name} must be a non-negative integer")) : null;
            public string Positional0(string what) => Positional.Count > 0 ? Positional[0] : throw new UsageException($"{what} is required");
            public void CheckAllUsed(int positionalAllowed)
            {
                var unknown = Options.Keys.Where(k => !used.Contains(k)).ToList();
                if (unknown.Count > 0) throw new UsageException($"unknown option --{unknown[0]}");
                if (Positional.Count > positionalAllowed) throw new UsageException($"unexpected argument '{Positional[positionalAllowed]}'");
            }
        }

        private static readonly HashSet<string> Flags = new() { "json", "latest", "text", "headings", "reveal", "save", "stdin", "lf", "allow-unknown-normalization", "help" };

        private static (string command, Args args) Parse(string[] argv)
        {
            var args = new Args();
            string? command = null;
            for (var i = 0; i < argv.Length; i++)
            {
                var a = argv[i];
                if (a.StartsWith("--"))
                {
                    var name = a.Substring(2);
                    if (Flags.Contains(name)) args.Options[name] = null;
                    else if (i + 1 < argv.Length) args.Options[name] = argv[++i];
                    else throw new UsageException($"--{name} needs a value");
                }
                else if (command == null) command = a;
                else args.Positional.Add(a);
            }
            return (command ?? "", args);
        }

        public async Task<int> RunAsync(string[] argv, CancellationToken cancellationToken = default)
        {
            bool json = argv.Contains("--json");
            try
            {
                var (command, args) = Parse(argv);
                if (command == "" || command == "help" || args.Options.ContainsKey("help"))
                {
                    stdout.Write(Help);
                    return command == "" && !args.Options.ContainsKey("help") ? Usage : Ok;
                }
                args.Flag("json");
                var endpoint = args.Value("endpoint") ?? DefaultEndpoint();
                var clientId = args.Value("client-id") ?? ClientId.Load();
                var (method, parameters, scopes) = Build(command, args);

                Stream stream;
                try { stream = await connect(endpoint, cancellationToken).ConfigureAwait(false); }
                catch (Exception e) when (e is TimeoutException || e is IOException || e is UnauthorizedAccessException)
                {
                    return Fail(json, NotRunning, null, $"Typedown is not running, or its automation switch is off ({e.Message}).");
                }
                using (stream)
                {
                    var framing = new MessageFraming(stream, 64L * 1024 * 1024);
                    var initialize = new JObject
                    {
                        ["apiVersion"] = AutomationSession.ApiVersion,
                        ["client"] = new JObject { ["id"] = clientId, ["name"] = "typedownctl", ["version"] = typeof(Cli).Assembly.GetName().Version?.ToString() ?? "" },
                        ["requestedScopes"] = new JArray(scopes),
                    };
                    var init = await CallAsync(framing, 1, "system.initialize", initialize, cancellationToken).ConfigureAwait(false);
                    if (init["error"] is JObject initError) return FailWith(json, initError);
                    var denied = init["result"]?["deniedScopes"] as JArray;
                    if (denied != null && denied.Count > 0)
                        return Fail(json, Incompatible, null, $"This Typedown does not grant: {string.Join(", ", denied.Select(d => d["scope"]))}.");
                    var reply = await CallAsync(framing, 2, method, parameters, cancellationToken).ConfigureAwait(false);
                    if (reply["error"] is JObject error) return FailWith(json, error);
                    var result = reply["result"] ?? JValue.CreateNull();
                    if (json) stdout.WriteLine(result.ToString(Formatting.None));
                    else stdout.Write(Describe(command, result));
                    return Ok;
                }
            }
            catch (UsageException e)
            {
                return Fail(json, Usage, null, e.Message + " (typedownctl help)");
            }
            catch (FramingException e)
            {
                return Fail(json, Other, null, $"The connection broke: {e.Message}");
            }
            catch (IOException e)
            {
                return Fail(json, NotRunning, null, $"The connection closed: {e.Message}");
            }
        }

        private (string method, JObject parameters, string[] scopes) Build(string command, Args a)
        {
            JObject p;
            switch (command)
            {
                case "status":
                    a.CheckAllUsed(0);
                    return ("app.getState", new JObject(), new[] { Scopes.AppRead });
                case "windows":
                    a.CheckAllUsed(0);
                    return ("window.list", new JObject(), new[] { Scopes.AppRead });
                case "documents":
                    p = new JObject();
                    if (a.Value("window") is string w) p["windowId"] = w;
                    a.CheckAllUsed(0);
                    return ("document.list", p, new[] { Scopes.DocumentRead });
                case "get":
                {
                    p = new JObject { ["documentId"] = a.Positional0("documentId"), ["consistency"] = a.Flag("latest") ? "latest" : "snapshot" };
                    var include = new JArray();
                    if (a.Flag("text")) include.Add("text");
                    if (a.Flag("headings")) include.Add("headings");
                    if (include.Count > 0) p["include"] = include;
                    a.CheckAllUsed(1);
                    return ("document.get", p, new[] { Scopes.DocumentRead });
                }
                case "open":
                    p = new JObject { ["path"] = Path.GetFullPath(a.Positional0("path")) };
                    if (a.Value("window") is string ow) p["windowId"] = ow;
                    var openScopes = Reveal(a, p, Scopes.DocumentWrite);
                    a.CheckAllUsed(1);
                    return ("document.open", p, openScopes);
                case "create":
                {
                    p = new JObject();
                    if (a.Value("window") is string cw) p["windowId"] = cw;
                    var text = ReadText(a, required: false);
                    if (text != null) p["text"] = text;
                    Policy(a, p);
                    var scopes = Reveal(a, p, Scopes.DocumentWrite);
                    a.CheckAllUsed(0);
                    return ("document.create", p, scopes);
                }
                case "replace":
                {
                    p = new JObject { ["documentId"] = a.Positional0("documentId"), ["baseRevision"] = a.RequiredLong("base-revision"), ["text"] = ReadText(a, required: true) };
                    return Write("document.replace", p, a);
                }
                case "replace-text":
                {
                    p = new JObject
                    {
                        ["documentId"] = a.Positional0("documentId"),
                        ["baseRevision"] = a.RequiredLong("base-revision"),
                        ["find"] = a.Required("find"),
                        ["replacement"] = a.Required("replacement"),
                        ["expectedCount"] = a.RequiredLong("expected-count"),
                    };
                    return Write("document.replaceText", p, a);
                }
                case "settings":
                {
                    var sub = a.Positional0("settings describe | get | set");
                    switch (sub)
                    {
                        case "describe":
                            a.CheckAllUsed(1);
                            return ("settings.describe", new JObject(), new[] { Scopes.SettingsRead });
                        case "get":
                            p = new JObject();
                            if (a.Positional.Count > 1) p["keys"] = new JArray(a.Positional.Skip(1));
                            return ("settings.get", p, new[] { Scopes.SettingsRead });
                        case "set":
                            if (a.Positional.Count < 3) throw new UsageException("settings set <key> <json value> --base-revision N");
                            JToken value;
                            try { value = JToken.Parse(a.Positional[2]); }
                            catch (JsonException) { value = a.Positional[2]; } // a bare word is a string: rtl, auto
                            p = new JObject { ["key"] = a.Positional[1], ["value"] = value, ["baseSettingsRevision"] = a.RequiredLong("base-revision") };
                            a.CheckAllUsed(3);
                            return ("settings.set", p, new[] { Scopes.SettingsWrite });
                        default:
                            throw new UsageException($"unknown settings command '{sub}'");
                    }
                }
                case "save":
                    p = new JObject { ["documentId"] = a.Positional0("documentId") };
                    if (a.OptionalLong("base-revision") is long r) p["baseRevision"] = r;
                    a.CheckAllUsed(1);
                    return ("document.save", p, new[] { Scopes.DocumentSave });
                default:
                    throw new UsageException($"unknown command '{command}'");
            }
        }

        private (string, JObject, string[]) Write(string method, JObject p, Args a)
        {
            Policy(a, p);
            if (a.Value("client-operation-id") is string op) p["clientOperationId"] = op;
            var scopes = new List<string> { Scopes.DocumentWrite };
            if (a.Flag("save")) { p["save"] = true; scopes.Add(Scopes.DocumentSave); }
            scopes.AddRange(Reveal(a, p).Where(s => !scopes.Contains(s)));
            a.CheckAllUsed(1);
            return (method, p, scopes.ToArray());
        }

        private static void Policy(Args a, JObject p)
        {
            // Accepting an unclassified normalization is the caller's explicit choice, never a default.
            if (a.Flag("allow-unknown-normalization")) p["normalizationPolicy"] = "allowUnknown";
        }

        private static string[] Reveal(Args a, JObject p, params string[] scopes)
        {
            if (!a.Flag("reveal")) return scopes;
            p["reveal"] = "document";
            return scopes.Append(Scopes.WindowFocus).ToArray();
        }

        private string? ReadText(Args a, bool required)
        {
            var file = a.Value("text-file");
            var fromStdin = a.Flag("stdin");
            var lf = a.Flag("lf");
            if (file != null && fromStdin) throw new UsageException("give --text-file or --stdin, not both");
            string? text = file != null ? File.ReadAllText(file, new UTF8Encoding(false)) : fromStdin ? stdin.ReadToEnd() : null;
            if (text == null && required) throw new UsageException("the text is required: --text-file F or --stdin");
            if (text != null && text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            if (text != null && lf) text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return text;
        }

        private static async Task<JObject> CallAsync(MessageFraming framing, int id, string method, JObject parameters, CancellationToken ct)
        {
            var message = new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters };
            await framing.WriteAsync(new UTF8Encoding(false).GetBytes(message.ToString(Formatting.None)), ct).ConfigureAwait(false);
            while (true)
            {
                var frame = await framing.ReadAsync(ct).ConfigureAwait(false);
                if (frame.Status == FrameStatus.EndOfStream) throw new IOException("Typedown closed the connection.");
                if (frame.Status != FrameStatus.Message) continue;
                var reply = JObject.Parse(new UTF8Encoding(false).GetString(frame.Body!));
                if (reply["id"]?.Type == JTokenType.Integer && (int)reply["id"]! == id) return reply;
                if (reply["id"]?.Type == JTokenType.Null && reply["error"] != null) return reply;
            }
        }

        public static int ExitCodeFor(string? kind) => kind switch
        {
            "unsupported_version" or "scope_required" or "not_initialized" or "method_not_found" => Incompatible,
            "window_not_found" or "document_not_found" => NotFound,
            "revision_conflict" or "match_count_mismatch" => Conflict,
            "editor_not_ready" or "content_sync_timeout" or "presentation_timeout" => NotReady,
            "save_failed" or "persistence_failed" => SaveFailed,
            "invalid_params" => Usage,
            _ => Other,
        };

        private int FailWith(bool json, JObject error)
        {
            var kind = (string?)error["data"]?["kind"];
            return Fail(json, ExitCodeFor(kind), error, $"{error["message"]} [{kind}]");
        }

        private int Fail(bool json, int exit, JObject? error, string message)
        {
            stderr.WriteLine("typedownctl: " + message);
            if (json)
            {
                error ??= new JObject { ["code"] = 0, ["message"] = message, ["data"] = new JObject { ["kind"] = exit == Usage ? "cli_usage" : exit == NotRunning ? "cli_not_connected" : "cli_error" } };
                stdout.WriteLine(new JObject { ["error"] = error }.ToString(Formatting.None));
            }
            return exit;
        }

        private static string Describe(string command, JToken result)
        {
            var sb = new StringBuilder();
            switch (command)
            {
                case "status":
                    sb.AppendLine($"Typedown {result["version"]}, {result["windowCount"]} window(s), active window {result["activeWindowId"]}");
                    break;
                case "windows":
                    foreach (var w in result["windows"]!) sb.AppendLine($"{w["windowId"]}  {(bool)w["active"]! switch { true => "active", false => "      " }}  {w["documentCount"]} document(s)");
                    break;
                case "documents":
                    foreach (var d in result["documents"]!)
                        sb.AppendLine($"{d["documentId"]}  r{d["revision"]}  {((bool)d["saved"]! ? "saved  " : "unsaved")}  {(string?)d["path"] ?? "(untitled) " + d["title"]}");
                    break;
                case "get":
                    if (result["text"] is JToken t) { sb.Append((string?)t); break; }
                    sb.AppendLine($"{result["documentId"]}  revision {result["revision"]}  {((bool)result["saved"]! ? "saved" : "unsaved")}  {(string?)result["path"] ?? "(untitled)"}");
                    sb.AppendLine($"contentHash {result["contentHash"]}  pendingNormalization {result["normalization"]?["pendingNormalization"]}");
                    if (result["headings"] is JArray hs) foreach (var h in hs) sb.AppendLine($"{new string('#', (int)h["level"]!)} {h["text"]}  ({h["slug"]})");
                    break;
                default:
                    foreach (var property in ((JObject)result).Properties().Where(p => p.Value.Type != JTokenType.Object))
                        sb.AppendLine($"{property.Name}: {property.Value}");
                    break;
            }
            return sb.ToString();
        }

        public static string DefaultEndpoint() => AutomationEndpoint.PipeName(BuildTypes.Application, CurrentUserId());

        public static string CurrentUserId()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
            return geteuid().ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        [DllImport("libc")]
        private static extern uint geteuid();

        private static async Task<Stream> ConnectPipeAsync(string endpoint, CancellationToken ct)
        {
            var pipe = new NamedPipeClientStream(".", endpoint, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(3000, ct).ConfigureAwait(false);
                return pipe;
            }
            catch
            {
                pipe.Dispose();
                throw;
            }
        }
    }

    /// <summary>The CLI's own client id: a UUID made once and kept (self-reported, for logs and the connection marker).</summary>
    public static class ClientId
    {
        public static string Load()
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "Typedown");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, "typedownctl-client-id");
                if (File.Exists(file) && Guid.TryParseExact(File.ReadAllText(file).Trim(), "D", out var existing)) return existing.ToString("D");
                var id = Guid.NewGuid().ToString("D");
                File.WriteAllText(file, id);
                return id;
            }
            catch
            {
                return Guid.NewGuid().ToString("D");
            }
        }
    }
}
