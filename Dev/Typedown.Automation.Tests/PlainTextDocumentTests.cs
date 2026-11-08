using Typedown.Core.Utilities;
using Xunit;

// Which files are shown as plain text, nothing in them parsed as Markdown (a reader's request, 2026-11-02).
public class PlainTextDocumentTests
{
    [Theory]
    [InlineData(@"C:\notes\todo.txt")]
    [InlineData(@"C:\notes\TODO.TXT")]
    [InlineData(@"C:\notes\readme.text")]
    [InlineData(@"C:\logs\app.log")]
    public void Plain_text_files(string path) => Assert.True(FileTypeHelper.IsPlainTextDocument(path));

    [Theory]
    [InlineData(@"C:\notes\note.md")]
    [InlineData(@"C:\notes\note.markdown")]
    [InlineData(@"C:\notes\note.mdtxt")]
    [InlineData(@"C:\notes\note.rmd")]
    [InlineData(@"C:\notes\no-extension")]
    [InlineData(null)]
    [InlineData("")]
    public void Markdown_files_and_new_documents_are_not(string? path) => Assert.False(FileTypeHelper.IsPlainTextDocument(path!));

    [Fact]
    public void A_plain_text_file_still_opens()
    {
        // Opened from the dialog and shown in the file tree as before: only how it is shown changes.
        Assert.True(FileTypeHelper.IsEditableTextFile(@"C:\notes\todo.txt"));
        Assert.True(FileTypeHelper.IsEditableTextFile(@"C:\logs\app.log"));
    }
}
