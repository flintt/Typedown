using System.Collections.Generic;
using System.IO;
using System.Linq;
using Typedown.Core.Utilities;
using Xunit;

namespace Typedown.Automation.Tests
{
    /// <summary>File > Check Document: pictures and linked files not there, links to headings the document lacks.</summary>
    public class DocumentCheckTests
    {
        private static readonly string Folder = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "doc-check"));

        // The files "there": a.png, images/b.png, notes.md, sub/.
        private static bool There(string path) =>
            new[] { "a.png", "images/b.png", "notes.md", "sub" }.Select(p => Path.GetFullPath(Path.Combine(Folder, p.Replace('/', Path.DirectorySeparatorChar)))).Contains(path);

        private static List<DocumentProblem> Run(string markdown, params string[] slugs) =>
            DocumentCheck.Run(markdown, Folder, Folder, DocumentCheck.HeadingMatcher(slugs.Select(s => (s, s))), There);

        [Fact]
        public void A_picture_not_there_is_named_with_its_line()
        {
            var problems = Run("# T\n\n![ok](a.png) ![gone](missing.png)\n\n<img src=\"images/b.png\"> <img src='images/gone.png'>\n");
            Assert.Equal(new[] { (DocumentProblemKind.MissingImage, 3, "missing.png"), (DocumentProblemKind.MissingImage, 5, "images/gone.png") },
                problems.Select(p => (p.Kind, p.Line, p.Target)));
            Assert.Equal("![gone](missing.png)", problems[0].Find);
        }

        [Fact]
        public void A_link_to_a_file_not_there_and_to_a_heading_the_document_lacks()
        {
            var problems = Run("[n](notes.md) [n2](notes.md#part) [s](sub) [x](old.md) [h](#intro) [h2](#missing) [enc](my%20file.md)\n\n[def]: gone/ref.md\n", "intro");
            Assert.Equal(new[] { "old.md", "#missing", "my%20file.md", "gone/ref.md" }, problems.Select(p => p.Target));
            Assert.Equal(DocumentProblemKind.MissingHeading, problems[1].Kind);
        }

        [Fact]
        public void The_web_mail_data_and_code_are_not_looked_at()
        {
            var problems = Run("![w](https://x.org/a.png) [m](mailto:a@b.c) ![d](data:image/png;base64,AAAA) `![c](gone.png)`\n\n```\n![f](gone.png)\n[f](gone.md)\n```\n\n~~~~md\n[x](gone.md)\n~~~~\n");
            Assert.Empty(problems);
        }

        [Fact]
        public void An_untitled_document_has_only_its_headings_looked_at()
        {
            var problems = DocumentCheck.Run("![p](gone.png) [h](#nowhere)", null, null, DocumentCheck.HeadingMatcher(new (string, string)[0]), There);
            Assert.Equal(DocumentProblemKind.MissingHeading, Assert.Single(problems).Kind);
        }

        [Fact]
        public void A_fragment_matches_a_heading_as_a_click_on_the_link_does()
        {
            // The heading "1.1 研究" (anchor 11-研究); "Intro" (intro). Written with the dots kept, as the text, or loosely.
            var has = DocumentCheck.HeadingMatcher(new[] { ("11-研究", "1.1 研究"), ("intro", "Intro") });
            Assert.True(has("intro") && has("Intro") && has("11-研究") && has("1.1-研究") && has("1.1 研究"));
            Assert.False(has("outro"));
            // The headings not known (source mode): links to headings are not looked at.
            Assert.Empty(DocumentCheck.Run("[h](#nowhere)", Folder, Folder, null, There));
        }
    }
}
