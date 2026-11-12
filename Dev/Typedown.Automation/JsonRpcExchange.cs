using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Typedown.Automation
{
    /// <summary>
    /// A client's one request and its reply over a framed connection: the request written, frames read until the reply
    /// with its id - or an error with a null id (one the server could not tie to a request) - comes; notifications and
    /// other replies in between passed over. The CLI's one-shot session and the MCP server's long one each wrote this loop
    /// (the modularity audit's M12); what is theirs stays theirs: the ids, the session's start, reconnecting, how an error
    /// is shown. One reader at a time on a connection: a caller does not run two of these at once on one framing.
    /// </summary>
    public static class JsonRpcExchange
    {
        private static readonly UTF8Encoding Utf8 = new(false);

        public static async Task<JObject> CallAsync(MessageFraming framing, int id, string method, JObject parameters, CancellationToken ct)
        {
            var message = new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters };
            await framing.WriteAsync(Utf8.GetBytes(message.ToString(Formatting.None)), ct).ConfigureAwait(false);
            while (true)
            {
                var frame = await framing.ReadAsync(ct).ConfigureAwait(false);
                if (frame.Status == FrameStatus.EndOfStream) throw new IOException($"{Brand.Name} closed the connection.");
                if (frame.Status != FrameStatus.Message) continue;
                var reply = JObject.Parse(Utf8.GetString(frame.Body!));
                if (reply["id"]?.Type == JTokenType.Integer && (int)reply["id"]! == id) return reply;
                if (reply["id"]?.Type == JTokenType.Null && reply["error"] != null) return reply;
            }
        }
    }
}
