// What the production files compiled here call from the parts of Typedown that need Windows.
namespace Typedown.Core.Utilities
{
    internal static class Log
    {
        public static readonly System.Collections.Generic.List<string> Lines = new();

        public static void Debug(string text) { lock (Lines) Lines.Add(text); }
    }
}
