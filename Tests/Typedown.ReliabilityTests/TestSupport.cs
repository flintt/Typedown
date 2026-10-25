using System.Security.Cryptography;
using System.Text;
using System.Numerics;

namespace Typedown.ReliabilityTests
{
    internal sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "typedown-tests-" + Guid.NewGuid().ToString("N"));

        public TestDirectory() => Directory.CreateDirectory(Path);

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    internal static class Repository
    {
        public static string Root
        {
            get
            {
                foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
                {
                    var directory = new DirectoryInfo(start);
                    while (directory != null)
                    {
                        if (System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "Typedown.sln")))
                            return directory.FullName;
                        directory = directory.Parent;
                    }
                }
                throw new DirectoryNotFoundException("Cannot locate the Typedown repository root.");
            }
        }
    }
}

// AutoBackup's defaults depend on the application environment and its stable path hash. Tests pass an explicit
// backup directory, while these small stubs let the real production source compile in the isolated test project.
namespace Typedown.Core
{
    internal static class Config
    {
        public static string GetLocalFolderPath() => System.IO.Path.GetTempPath();
    }
}

namespace Typedown.Core.Utilities
{
    internal static class Common
    {
        public static string SimpleHash2(string value)
        {
            // Match the stable application's backup-file key exactly. A convenient
            // test-only hash would make recovery tests pass while naming files the
            // migrated host can no longer find.
            var dividend = new BigInteger(MD5.HashData(Encoding.UTF8.GetBytes(value)));
            const string alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
            var builder = new StringBuilder();
            while (dividend != 0)
            {
                dividend = BigInteger.DivRem(dividend, 36, out var remainder);
                builder.Insert(0, alphabet[Math.Abs((int)remainder)]);
            }
            var result = builder.ToString();
            return result.Substring(0, Math.Min(6, result.Length));
        }
    }
}
