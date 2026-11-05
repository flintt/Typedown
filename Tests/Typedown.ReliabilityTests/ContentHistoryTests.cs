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
        public void TheVisualEditorsSelectionHeardOfThroughItsSelectionChange()
        {
            // The visual editor reports no selection as a caret, only the caret after a change; the host hears of the
            // selection from the page's SelectionChange (W10 job 196: one Ctrl+Z still took the typing away).
            var h = Typed();
            h.SelectionMade();
            h.ContentChange("# T\n\n\n\n");
            h.CursorChange(Caret(3, 0));
            var back = h.Undo();
            Assert.AreEqual("# T\n\nhello world\n", back?.Text, "one undo puts back the paragraph as typed");
            Assert.AreEqual(Caret(2, 11), back.Cursor, "with the caret the typing left");
            Assert.AreEqual("# T\n\n", h.Undo()?.Text);
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

        // ---- steps by the kind of edit (the page tells it; the user, 2026-11-05, #39) ----

        private const ContentHistory.EditKind Insert = ContentHistory.EditKind.Insert;
        private const ContentHistory.EditKind Delete = ContentHistory.EditKind.Delete;
        private const ContentHistory.EditKind Other = ContentHistory.EditKind.Other;
        private const ContentHistory.EditKind External = ContentHistory.EditKind.External;

        // The paragraph after the heading, as the page reports it: the text and then its caret, all within 3 s.
        private static void Report(ContentHistory h, string paragraph, ContentHistory.EditKind kind, bool moved = false, string inserted = null)
        {
            h.ContentChange("# T\n\n" + paragraph + "\n", kind, moved, inserted);
            h.CursorChange(Caret(2, paragraph.Length));
        }

        private static ContentHistory Fresh()
        {
            var h = new ContentHistory();
            h.InitHistory("# T\n\n");
            h.CursorChange(Caret(2, 0));
            return h;
        }

        private static string TypeLetters(ContentHistory h, string start, string letters)
        {
            var text = start;
            foreach (var letter in letters)
            {
                text += letter;
                Report(h, text, Insert, inserted: letter.ToString());
            }
            return text;
        }

        [TestMethod]
        public void ALongParagraphTypedFastIsSeveralSteps()
        {
            // One line in Markdown and no pause: before, one step however long. Now a step is at most TypingStepChars.
            var h = Fresh();
            var letters = new string('a', ContentHistory.TypingStepChars * 2 + 10);
            TypeLetters(h, "", letters);
            Assert.AreEqual("# T\n\n" + letters[..(ContentHistory.TypingStepChars * 2)] + "\n", h.Undo()?.Text);
            Assert.AreEqual("# T\n\n" + letters[..ContentHistory.TypingStepChars] + "\n", h.Undo()?.Text);
            Assert.AreEqual("# T\n\n", h.Undo()?.Text);
        }

        [TestMethod]
        public void ASentencesEndEndsATypingStep()
        {
            var h = Fresh();
            var text = TypeLetters(h, "", "One. ");
            TypeLetters(h, text, "Two");
            Assert.AreEqual("# T\n\nOne. \n", h.Undo()?.Text, "the sentence typed before is kept");
            Assert.AreEqual("# T\n\n", h.Undo()?.Text);
        }

        [TestMethod]
        public void AFormatAPasteOrAWordDeletedAfterTypingIsAStepOfItsOwn()
        {
            foreach (var (name, after) in new[] { ("Ctrl+B", "**hello**"), ("a paste", "hello pasted"), ("Ctrl+Backspace", ""), ("a list dragged", "- hello") })
            {
                var h = Fresh();
                TypeLetters(h, "", "hello");
                Report(h, after, Other);
                Assert.AreEqual("# T\n\nhello\n", h.Undo()?.Text, name + ": one undo takes it back alone");
                Assert.AreEqual("# T\n\n", h.Undo()?.Text, name + ": a second the typing");
                // And typing after it is a step after it, not merged into it.
                h = Fresh();
                TypeLetters(h, "", "hello");
                Report(h, after, Other);
                TypeLetters(h, after, "!");
                Assert.AreEqual("# T\n\n" + after + "\n", h.Undo()?.Text, name + ": typing after it is a step of its own");
            }
        }

        [TestMethod]
        public void TypingAfterTheCaretWasMovedWithinTheLineIsANewStep()
        {
            var h = Fresh();
            TypeLetters(h, "", "hello world");
            // Home, then letters at the start of the line: the same line, under 3 s.
            Report(h, "X" + "hello world", Insert, moved: true, inserted: "X");
            Assert.AreEqual("# T\n\nhello world\n", h.Undo()?.Text);
        }

        [TestMethod]
        public void DeletingIsAStepApartFromTheTypingBeforeIt()
        {
            var h = Fresh();
            TypeLetters(h, "", "hello");
            Report(h, "hell", Delete);
            Report(h, "hel", Delete);
            Assert.AreEqual("# T\n\nhello\n", h.Undo()?.Text, "the Backspaces are one step");
            Assert.AreEqual("# T\n\n", h.Undo()?.Text, "the typing another");
        }

        [TestMethod]
        public void AChangeNotTheReadersIsNeverMergedWithTheirs()
        {
            // An automation write (or a reload) three seconds or less after the reader's typing.
            var h = Fresh();
            TypeLetters(h, "", "mine");
            h.CommitPending();
            Report(h, "mine theirs", External);
            TypeLetters(h, "mine theirs", " again");
            Assert.AreEqual("# T\n\nmine theirs\n", h.Undo()?.Text, "the reader's typing after it");
            Assert.AreEqual("# T\n\nmine\n", h.Undo()?.Text, "the write");
            Assert.AreEqual("# T\n\n", h.Undo()?.Text, "the reader's typing before it");
            Assert.AreEqual("# T\n\nmine\n", h.Redo()?.Text);
            Assert.AreEqual("# T\n\nmine theirs\n", h.Redo()?.Text);
        }

        [TestMethod]
        public void AReportThatDoesNotSayKeepsTheStepsAsBefore()
        {
            // An editor's own re-render while typing (no kind): merged with the typing, as every report was before.
            var h = Fresh();
            TypeLetters(h, "", "hello");
            Report(h, "hello", ContentHistory.EditKind.Unknown);
            Report(h, "hello!", ContentHistory.EditKind.Unknown);
            Assert.AreEqual("# T\n\n", h.Undo()?.Text);
        }
    }
}
