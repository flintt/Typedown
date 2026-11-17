using System;
using Typedown.Automation;
using System.IO;
using System.Text;
using Typedown.Mcp;

namespace Typedown.Cli
{
    /// <summary>typedownctl mcp [--endpoint NAME]: an MCP server on stdin/stdout. Diagnostics go to stderr only.</summary>
    public static class McpHost
    {
        public static int Run(string[] args)
        {
            var endpoint = Cli.DefaultEndpoint();
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--endpoint" && i + 1 < args.Length) endpoint = args[++i];
                else if (args[i] == "--help" || args[i] == "-h")
                {
                    Console.Error.WriteLine($"{Brand.CliName} mcp [--endpoint NAME] - MCP server (stdio) for a running {Brand.Name} with local automation on.");
                    return 0;
                }
            }
            var utf8 = new UTF8Encoding(false);
            var input = new StreamReader(Console.OpenStandardInput(), utf8);
            var output = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = false, NewLine = "\n" };
            var server = new McpServer(input, output, name => new TypedownConnection(endpoint, McpClientId.Load(), name, Cli.ConnectPipeAsync));
            // Run from an installed package: gone with the package's update, which it would otherwise keep from starting.
            using var superseded = PackageSuperseded.Watch(PackageSuperseded.PackageOf(AppContext.BaseDirectory), PackageSuperseded.Registered, () =>
            {
                Console.Error.WriteLine($"{Brand.Name} was updated: this MCP server (the old version's) ends; start it again for the new one.");
                Environment.Exit(0);
            }, TimeSpan.FromSeconds(3));
            server.RunAsync().GetAwaiter().GetResult();
            return 0;
        }
    }
}
