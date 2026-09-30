using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Typedown.Mcp
{
    public static class Program
    {
        /// <summary>typedown-mcp [--endpoint NAME]: an MCP server on stdin/stdout. Diagnostics go to stderr only.</summary>
        public static async Task<int> Main(string[] args)
        {
            var endpoint = Cli.Cli.DefaultEndpoint();
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--endpoint" && i + 1 < args.Length) endpoint = args[++i];
                else if (args[i] is "--help" or "-h")
                {
                    Console.Error.WriteLine("typedown-mcp [--endpoint NAME] - MCP server (stdio) for a running Typedown with local automation on.");
                    return 0;
                }
            }
            var utf8 = new UTF8Encoding(false);
            var input = new StreamReader(Console.OpenStandardInput(), utf8);
            var output = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = false, NewLine = "\n" };
            var server = new McpServer(input, output, name => new TypedownConnection(endpoint, McpClientId.Load(), name, Cli.Cli.ConnectPipeAsync));
            await server.RunAsync().ConfigureAwait(false);
            return 0;
        }
    }

    /// <summary>This server's own client id: a UUID made once and kept, like typedownctl's.</summary>
    public static class McpClientId
    {
        public static string Load()
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "Typedown");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, "typedown-mcp-client-id");
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
