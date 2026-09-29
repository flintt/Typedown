using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Typedown.Automation.Tests
{
    /// <summary>The application and the automation test host stay apart in the sources, not only by convention.</summary>
    public class BuildSeparationTests
    {
        private static readonly string Dev = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));

        [Fact]
        public void No_application_project_references_the_test_host()
        {
            var projects = Directory.GetFiles(Dev, "*.csproj", SearchOption.AllDirectories)
                .Where(p => !p.Contains("Typedown.Automation.Tests") && !p.Contains("Typedown.Automation.TestHost") && !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .ToList();
            Assert.Contains(projects, p => p.EndsWith("Typedown.Core.csproj"));
            foreach (var project in projects)
                Assert.DoesNotContain("Automation.TestHost", File.ReadAllText(project));
        }

        [Fact]
        public void The_shared_library_names_no_test_method()
        {
            foreach (var file in Directory.GetFiles(Path.Combine(Dev, "Typedown.Automation"), "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains("/obj/")))
                Assert.DoesNotContain("\"test.barrier", File.ReadAllText(file));
        }
    }
}
