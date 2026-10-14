using Microsoft.VisualStudio.TestTools.UnitTesting;
using Typedown.Core.Utilities;

namespace Typedown.ReliabilityTests
{
    /// <summary>
    /// Todo #16: the start-up's stages in debug.log, each once, in milliseconds since the process started, so a report
    /// of a slow start says where the time went.
    /// </summary>
    [TestClass]
    public class StartupTimingTests
    {
        [TestMethod]
        public void EachStageIsWrittenOnceWithItsTimeAndTheStepFromTheOneBefore()
        {
            var lines = new List<string>();
            var timing = new StartupTiming(new DateTime(2026, 10, 14, 12, 0, 0), lines.Add);

            timing.Mark("launched", new DateTime(2026, 10, 14, 12, 0, 0, 300));
            timing.Mark("window shown", new DateTime(2026, 10, 14, 12, 0, 0, 800));
            timing.Mark("window shown", new DateTime(2026, 10, 14, 12, 0, 5, 0));
            timing.Mark("document shown", new DateTime(2026, 10, 14, 12, 0, 1, 900));

            CollectionAssert.AreEqual(new[]
            {
                "startup: launched at 300 ms (+300)",
                "startup: window shown at 800 ms (+500)",
                "startup: document shown at 1900 ms (+1100)",
            }, lines);
        }

        [TestMethod]
        public void NothingIsWrittenOnceTheStartIsOver()
        {
            var lines = new List<string>();
            var timing = new StartupTiming(new DateTime(2026, 10, 14, 12, 0, 0), lines.Add);
            timing.Mark("document shown", new DateTime(2026, 10, 14, 12, 0, 2), last: true);
            // A second window's editor, a page reloaded later: not the start.
            timing.Mark("editor page loaded", new DateTime(2026, 10, 14, 12, 1, 0));
            Assert.AreEqual(1, lines.Count);
        }

        [TestMethod]
        public void AnUnknownProcessStartWritesNothing()
        {
            var lines = new List<string>();
            var timing = new StartupTiming(null, lines.Add);
            timing.Mark("launched", DateTime.Now);
            Assert.AreEqual(0, lines.Count);
        }
    }
}
