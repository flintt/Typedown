using System;
using System.Text;

namespace Typedown.Cli
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.InputEncoding = new UTF8Encoding(false);
            return new Cli(Console.In, Console.Out, Console.Error).RunAsync(args).GetAwaiter().GetResult();
        }
    }
}
