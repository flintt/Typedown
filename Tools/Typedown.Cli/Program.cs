using System;
using System.Linq;
using System.Text;
using Typedown.Automation;

namespace Typedown.Cli
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.InputEncoding = new UTF8Encoding(false);
            var mcp = args.Length > 0 && args[0] == "mcp";
            // Run from an installed package, any command - the MCP server above all, which runs as long as its agent: gone
            // with the package's update, which it would otherwise keep from starting (PackageSuperseded). Three of them,
            // started by nothing found on the machine, kept an update from starting (2026-11-17).
            using var superseded = PackageSuperseded.Watch(PackageSuperseded.PackageOf(AppContext.BaseDirectory), PackageSuperseded.Registered, () =>
            {
                Console.Error.WriteLine(mcp
                    ? $"{Brand.Name} was updated: this MCP server (the old version's) ends; start it again for the new one."
                    : $"{Brand.Name} was updated while this ran: the old version's {Brand.CliName} ends; run it again.");
                Environment.Exit(mcp ? Cli.Ok : Cli.NotRunning);
            }, TimeSpan.FromSeconds(3));
            // typedownctl mcp [--endpoint NAME]: the MCP server (stdio) for AI agents, in the same program.
            if (mcp) return McpHost.Run(args.Skip(1).ToArray());
            return new Cli(Console.In, Console.Out, Console.Error).RunAsync(args).GetAwaiter().GetResult();
        }
    }
}
