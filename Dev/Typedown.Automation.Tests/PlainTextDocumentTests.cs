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
    // Config and data files (2026-11-04): opened as they are, not as Markdown.
    [InlineData(@"C:\app\settings.ini")]
    [InlineData(@"C:\app\Settings.JSON")]
    [InlineData(@"C:\app\app.cfg")]
    [InlineData(@"C:\etc\nginx.conf")]
    [InlineData(@"C:\app\pyproject.toml")]
    [InlineData(@"C:\app\ci.yaml")]
    [InlineData(@"C:\app\ci.yml")]
    [InlineData(@"C:\app\layout.xml")]
    [InlineData(@"C:\data\sales.csv")]
    [InlineData(@"C:\data\sales.tsv")]
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
        Assert.True(FileTypeHelper.IsEditableTextFile(@"C:\app\settings.ini"));
        Assert.True(FileTypeHelper.IsEditableTextFile(@"C:\app\package.json"));
        Assert.False(FileTypeHelper.IsMarkdownFile(@"C:\app\package.json"));
    }

    [Theory]
    [InlineData(@"C:\app\tool.exe")]
    [InlineData(@"C:\app\data.db")]
    [InlineData(@"C:\docs\report.docx")]
    public void Binary_files_are_not_opened(string path) => Assert.False(FileTypeHelper.IsEditableTextFile(path));
}
