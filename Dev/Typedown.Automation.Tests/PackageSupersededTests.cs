using System;
using System.Threading;
using System.Threading.Tasks;
using Typedown.Cli;
using Xunit;

namespace Typedown.Automation.Tests
{
    // The MCP server started from an installed package ends when the package is updated: alive, it kept the old version's
    // container and the new version would not start (0x80070020, 2026-11-17).
    public class PackageSupersededTests
    {
        private const string Name = "publisher.App_1.4.2.38_x64__9a2b25czy3tfj";

        [Theory]
        [InlineData(@"C:\Program Files\WindowsApps\" + Name + @"\", Name)]
        [InlineData(@"C:\Program Files\WindowsApps\" + Name + @"\cli\", Name)]
        [InlineData(@"C:\Program Files\App\", null)]
        [InlineData(@"C:\Users\me\src\App\bin\Debug\", null)]
        public void The_package_is_the_folder_under_WindowsApps(string baseDirectory, string? expected)
        {
            if (!OperatingSystem.IsWindows()) baseDirectory = baseDirectory.Replace('\\', '/').Replace("C:", "");
            Assert.Equal(expected, PackageSuperseded.PackageOf(baseDirectory));
        }

        [Fact]
        public async Task Gone_once_the_package_is_no_longer_registered()
        {
            var registered = true;
            var gone = new TaskCompletionSource<bool>();
            var calls = 0;
            using var timer = PackageSuperseded.Watch(Name, _ => registered, () => { Interlocked.Increment(ref calls); gone.TrySetResult(true); }, TimeSpan.FromMilliseconds(20));
            Assert.NotNull(timer);
            await Task.Delay(100);
            Assert.False(gone.Task.IsCompleted);
            registered = false;
            Assert.True(await Task.WhenAny(gone.Task, Task.Delay(5000)) == gone.Task, "gone within 5 s of the update");
            await Task.Delay(100);
            Assert.Equal(1, calls);
        }

        [Fact]
        public void A_package_not_installed_is_not_registered_on_Windows()
        {
            // The registry read itself (advapi32): only Windows has one; elsewhere every package counts as registered.
            Assert.Equal(!OperatingSystem.IsWindows(), PackageSuperseded.Registered("no.such.package_1.0.0.0_x64__0000000000000"));
        }

        [Fact]
        public void Nothing_watched_off_a_package_or_without_a_registration_to_begin_with()
        {
            Assert.Null(PackageSuperseded.Watch(null, _ => true, () => throw new InvalidOperationException(), TimeSpan.FromMilliseconds(10)));
            Assert.Null(PackageSuperseded.Watch(Name, _ => false, () => throw new InvalidOperationException(), TimeSpan.FromMilliseconds(10)));
        }
    }
}
