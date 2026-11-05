using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Typedown.Core.Models;

namespace Typedown.Core.Models
{
    // The commit timer for the tests: started and stopped as the window's would be, fired by the test (a pause in typing).
    public partial class ContentHistory
    {
        public bool TimerRunning { get; private set; }

        partial void InitTimer() { }

        partial void StartTimer(TimeSpan interval) => TimerRunning = true;

        partial void StopTimer() => TimerRunning = false;

        public void Pause()
        {
            if (TimerRunning) { TimerRunning = false; CommitPending(); }
        }
    }
}

namespace Typedown.ReliabilityTests
{
    /// <summary>
    /// The editor's undo steps (ContentHistory): what one Ctrl+Z puts back. The editor page reports the caret and the text
    /// as they change; the history makes steps of them.
    /// </summary>
    [TestClass]
    public class ContentHistoryTests
    {
        private static CursorState Caret(int line, int ch) => new(new(line, ch), new(line, ch));

        private static CursorState Selection(int line, int from, int to) => new(new(line, from), new(line, to));

        // Typed into a document "# T\n\n" just loaded: the letters of "hello world", one change each.
        private static ContentHistory Typed(string typed = "hello world")
        {
            // The caret in the empty paragraph first, as the editor has it before the first letter.
            var h = new ContentHistory();
            h.InitHistory("# T\n\n");
            h.CursorChange(Caret(2, 0));
            for (var i = 1; i <= typed.Length; i++)
            {
                h.ContentChange("# T\n\n" + typed[..i] + "\n");
                h.CursorChange(Caret(2, i));
            }
            return h;
        }

        [TestMethod]
        public void EnterOverASelectionInOneLineIsUndoneAloneAndTheTypingStays()
        {
            // The user's steps (2026-11-05, #39): a paragraph typed, all of it selected (one line), Enter clears it.
            var h = Typed();
            h.CursorChange(Selection(2, 0, 11));
            h.ContentChange("# T\n\n\n\n");
            h.CursorChange(Caret(3, 0));
            var back = h.Undo();
            Assert.AreEqual("# T\n\nhello world\n", back?.Text, "one undo puts back the paragraph as typed");
            Assert.AreEqual(Selection(2, 0, 11), back.Cursor, "with the selection it had");
            Assert.AreEqual("# T\n\n", h.Undo()?.Text, "a second undo takes the typing away");
            Assert.AreEqual("# T\n\nhello world\n", h.Redo()?.Text);
            Assert.AreEqual("# T\n\n\n\n", h.Redo()?.Text);
        }

        [TestMethod]
        public void EveryChangeOverASelectionIsAStepOfItsOwn()
        {
            // A letter typed over it, Backspace over it, a paste over it, a cut: the text before each is one undo away.
            foreach (var (name, after) in new[] { ("a letter", "# T\n\nX\n"), ("Backspace", "# T\n\n\n"), ("a paste", "# T\n\npasted text\n"), ("a cut", "# T\n\nhello \n") })
            {
                var h = Typed();
                h.CursorChange(name == "a cut" ? Selection(2, 6, 11) : Selection(2, 0, 11));
                h.ContentChange(after);
                Assert.AreEqual("# T\n\nhello world\n", h.Undo()?.Text, name);
            }
        }

        [TestMethod]
        public void TheCaretReportedBeforeTheChangeStillFindsTheSelection()
        {
            // Source mode reports the caret before the change it goes with (KY01): the selection is not lost in between.
            var h = Typed();
            h.CursorChange(Selection(2, 0, 11));
            h.CursorChange(Caret(2, 0));
            h.ContentChange("# T\n\n\n");
            Assert.AreEqual("# T\n\nhello world\n", h.Undo()?.Text);
        }

        [TestMethod]
        public void TypingWithoutASelectionIsStillOneStepUntilAPause()
        {
            var h = Typed("abc");
            h.ContentChange("# T\n\nabcd\n");
            h.CursorChange(Caret(2, 4));
            Assert.AreEqual("# T\n\n", h.Undo()?.Text, "the letters typed together are one step");
            h = Typed("abc");
            h.Pause();
            h.ContentChange("# T\n\nabcd\n");
            h.CursorChange(Caret(2, 4));
            Assert.AreEqual("# T\n\nabc\n", h.Undo()?.Text, "a pause ends a step");
        }

        [TestMethod]
        public void ASelectionAlreadyAStepIsNotDoubled()
        {
            // Typing paused (a step), then selected and replaced: undo back to the typing, then to the start - no empty step.
            var h = Typed();
            h.Pause();
            h.CursorChange(Selection(2, 0, 11));
            h.ContentChange("# T\n\n\n\n");
            Assert.AreEqual("# T\n\nhello world\n", h.Undo()?.Text);
            Assert.AreEqual("# T\n\n", h.Undo()?.Text);
            Assert.IsNull(h.Undo());
        }
    }
}
