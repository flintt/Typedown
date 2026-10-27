using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// CB01 and CB02: the copy buttons of a display formula and of a table (the page's ContentState.copyBlock), and the
/// context menu's Copy table, used with the real mouse in the dark theme, as DG03 uses a diagram's.
/// </summary>
internal static partial class Program
{
    private const string Cb01Formula = "$$\n\\int_0^1 x^2 \\, dx = \\frac{1}{3}\n$$";
    // Written compact, as a reader writes it: the copy is the table as the document has it, not as the editor pads it.
    private const string Cb02Table = "|Name|**Score**|Note|\n|:-|:-:|-:|\n|*Ann*|`10`|[site](https://example.com)|\n|Bob|$x^2$|~~old~~|";

    /// <summary>
    /// CB01: a display formula's copy button. The pointer over the formula shows it (an inline formula has none); a click
    /// puts one item on the Windows clipboard: a picture of the formula (CF_DIB, mostly white: black on white in the dark
    /// theme), RTF with the picture (what Word takes before plain text) and no HTML (Word would take that before both),
    /// and the formula's Markdown as the document has it ($$ ... $$) as text. The document, its revision and its saved
    /// state stay as they were and the formula's source is not opened. Where Word is installed the copy pastes as one
    /// picture or one equation; then, for the record only, what Word makes of the formula's MathML put on the clipboard
    /// in HTML and as text (the editable-equation question). Pasted back into the document it is the formula again.
    /// </summary>
    private static async Task CB01(List<string> notes)
    {
        using var c = await Session("e2e CB01");
        var path = Fixture("cb01.md", "# CB01\n\nBefore the formula, with $a+b$ inline.\n\n" + Cb01Formula + "\n\nAfter the formula.\n");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var themeBefore = (int?)(await c.Call("test.settings.get", new { windowId, name = "AppTheme" }))["value"];
        string? Eval(JToken r) => (string?)r["result"];
        try
        {
            await c.Call("test.settings.set", new { windowId, name = "AppTheme", value = 2 });
            const string figure = "document.querySelector('#ag-editor-id figure[data-role=MULTIPLEMATH]')";
            var drawn = false;
            for (var i = 0; i < 50 && !drawn; i++)
            {
                await Task.Delay(200);
                drawn = Eval(await c.Call("test.editor.eval", new { windowId, script = $"String(!!{figure}?.querySelector('.ag-container-preview .katex'))" })) == "true";
            }
            Check(drawn, "the formula is drawn");
            Check(Eval(await c.Call("test.editor.eval", new { windowId, script = "String(!!document.querySelector('#ag-editor-id p .katex') && !document.querySelector('#ag-editor-id p .ag-diagram-copy'))" })) == "true",
                "the inline formula is drawn and has no copy button");
            var before = await Get(c, id);
            var text = (string)before["text"]!;
            var revision = await Revision(c, id);

            EmptyTheClipboard();
            await Activate(window);
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            await c.Call("test.editor.eval", new { windowId, script = $"{figure}.scrollIntoView({{ block: 'center' }})" });
            await Task.Delay(400);
            await MovePointerTo(await PagePointOnScreen(c, windowId, $"(() => {{ const r = {figure}.querySelector('.ag-container-preview').getBoundingClientRect(); return {{ x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) }} }})()"));
            var opacity = Eval(await c.Call("test.editor.eval", new { windowId, script = $"getComputedStyle({figure}.querySelector('.ag-diagram-copy')).opacity" }));
            notes.Add($"the button's opacity with the pointer over the formula: {opacity}");
            Check(double.TryParse(opacity, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var o) && o >= 0.4, $"the pointer over the formula shows its copy button (opacity {opacity})");

            await MovePointerTo(await PagePointOnScreen(c, windowId, $"(() => {{ const r = {figure}.querySelector('.ag-diagram-copy').getBoundingClientRect(); return {{ x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) }} }})()"));
            await LeftClick();

            string? clip = null;
            for (var i = 0; i < 50 && clip != Cb01Formula; i++) { await Task.Delay(200); clip = ClipboardText(); }
            Check(clip == Cb01Formula, $"the clipboard's text is the formula's Markdown, $$ ... $$ (got {JsonConvert.SerializeObject(clip)})");
            var picture = ClipboardDib();
            notes.Add(picture == null ? "no CF_DIB on the clipboard" : $"CF_DIB {picture.Value.width}x{picture.Value.height}, {picture.Value.bits} bits, {picture.Value.white:P0} of the pixels white");
            notes.Add("clipboard formats: " + string.Join(", ", ClipboardFormatNames()));
            Check(picture is { width: >= 100, height: >= 40 }, "the clipboard holds a picture of the formula (CF_DIB, at least 100x40)");
            Check(picture is { white: >= 0.6 and < 1 }, "the picture is mostly white with ink on it: black on white, in the dark theme too");
            Check(ClipboardHasFormat("Rich Text Format"), "the picture is there as RTF too (Word takes it before the text)");
            Check(ClipboardHtml() == null, "no HTML on the clipboard (Word would take it before the picture)");
            notes.Add("the button shows it copied: " + Eval(await c.Call("test.editor.eval", new { windowId, script = $"String({figure}.querySelector('.ag-diagram-copy').classList.contains('ag-code-copied'))" })));
            Check(Eval(await c.Call("test.editor.eval", new { windowId, script = $"String({figure}.classList.contains('ag-active'))" })) == "false", "the formula's source was not opened for editing");

            if (PasteIntoWord(System.IO.Path.GetDirectoryName(path)!) is { } word)
            {
                notes.Add($"Word: {word.shapes} picture(s), {word.media} embedded, {word.equations} equation(s); {word.detail}");
                Check(word.equations >= 1 || word.shapes == 1 && word.media == 1, "Word: the copy pastes as one picture or one equation");
            }
            else
                notes.Add("Word: not installed, not tried");

            await Task.Delay(800);
            var after = await Get(c, id);
            Check((string)after["text"]! == text, "the document's text is as it was");
            Check(await Revision(c, id) == revision, "the document's revision is as it was (no edit, nothing in its history)");
            Check((bool)after["saved"]! == (bool)before["saved"]!, "the document's saved state is as it was");

            // Pasted back into the document (the copy is still on the clipboard): the formula again.
            await PasteAtTheEnd(c, windowId, window);
            string pasted = "";
            for (var i = 0; i < 30 && Regex.Matches(pasted, Regex.Escape("\\int_0^1 x^2")).Count < 2; i++) { await Task.Delay(200); pasted = (string)(await Get(c, id))["text"]!; }
            notes.Add("after the paste: " + JsonConvert.SerializeObject(pasted));
            Check(Regex.Matches(pasted, Regex.Escape(Cb01Formula)).Count == 2 && !pasted.Contains("<img"), "pasting the copy back gives the formula's Markdown again");

            // For the record (not a check): would Word make an editable equation of the formula's MathML (KaTeX's, the
            // one in the page) - put on the clipboard in HTML, and as text alone?
            if (Type.GetTypeFromProgID("Word.Application") != null)
            {
                var mathml = Eval(await c.Call("test.editor.eval", new { windowId, script = $"{figure}.querySelector('.katex-mathml math')?.outerHTML || ''" })) ?? "";
                if (mathml.Length > 0)
                {
                    PutOnClipboard("x", "<p>" + mathml + "</p>");
                    var html = PasteIntoWord(System.IO.Path.GetDirectoryName(path)!);
                    PutOnClipboard(mathml, null);
                    var plain = PasteIntoWord(System.IO.Path.GetDirectoryName(path)!);
                    notes.Add($"MathML probe: in HTML -> {html?.equations} equation(s), {html?.shapes} picture(s); as text -> {plain?.equations} equation(s)");
                }
            }
        }
        finally
        {
            if (themeBefore != null) await c.Call("test.settings.set", new { windowId, name = "AppTheme", value = themeBefore });
        }
    }

    /// <summary>
    /// CB02: a table's copy button and the context menu's Copy table. The pointer over the table shows the button just
    /// above its top right corner; a click puts one item on the clipboard: HTML (CF_HTML) with the table - three header
    /// cells in &lt;th&gt;, the columns' alignment, bold, italic, code and a link - and the table's Markdown as the
    /// document has it (compact, as written) as text; no picture. Word pastes it as one table of 3 rows and 3 columns
    /// with the header's texts. The document, its revision and saved state stay as they were. Right-clicked in a cell
    /// the menu has Copy table (on a paragraph it has not), which copies the same; pasted back into the document it is
    /// the table's Markdown again, not its HTML turned into Markdown.
    /// </summary>
    private static async Task CB02(List<string> notes)
    {
        using var c = await Session("e2e CB02");
        var path = Fixture("cb02.md", "# CB02\n\nBefore the table.\n\n" + Cb02Table + "\n\nAfter the table.\n");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var themeBefore = (int?)(await c.Call("test.settings.get", new { windowId, name = "AppTheme" }))["value"];
        string? Eval(JToken r) => (string?)r["result"];
        try
        {
            await c.Call("test.settings.set", new { windowId, name = "AppTheme", value = 2 });
            const string figure = "document.querySelector('#ag-editor-id figure[data-role=TABLE]')";
            var drawn = false;
            for (var i = 0; i < 50 && !drawn; i++)
            {
                await Task.Delay(200);
                drawn = Eval(await c.Call("test.editor.eval", new { windowId, script = $"String(!!{figure}?.querySelector('tbody td'))" })) == "true";
            }
            Check(drawn, "the table is drawn");
            var before = await Get(c, id);
            var text = (string)before["text"]!;
            var revision = await Revision(c, id);

            EmptyTheClipboard();
            await Activate(window);
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            await c.Call("test.editor.eval", new { windowId, script = $"{figure}.scrollIntoView({{ block: 'center' }})" });
            await Task.Delay(400);
            await MovePointerTo(await PagePointOnScreen(c, windowId, $"(() => {{ const r = {figure}.querySelector('tbody td').getBoundingClientRect(); return {{ x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) }} }})()"));
            var place = Eval(await c.Call("test.editor.eval", new { windowId, script = $"(() => {{ const b = {figure}.querySelector('.ag-diagram-copy').getBoundingClientRect(), t = {figure}.querySelector('table').getBoundingClientRect(); return JSON.stringify({{ opacity: +getComputedStyle({figure}.querySelector('.ag-diagram-copy')).opacity, right: Math.round(b.right - t.right), above: b.bottom <= t.top + 1 }}) }})()" }));
            notes.Add($"the button with the pointer over the table: {place}");
            var shown = JObject.Parse(place ?? "{}");
            Check((double?)shown["opacity"] >= 0.4, "the pointer over the table shows its copy button");
            Check(Math.Abs((int?)shown["right"] ?? 99) <= 2 && (bool?)shown["above"] == true, "the button sits just above the table's top right corner, over no cell");

            await MovePointerTo(await PagePointOnScreen(c, windowId, $"(() => {{ const r = {figure}.querySelector('.ag-diagram-copy').getBoundingClientRect(); return {{ x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) }} }})()"));
            await LeftClick();
            await CheckTableOnClipboard("the button", notes);
            notes.Add("clipboard formats: " + string.Join(", ", ClipboardFormatNames()));
            Check(ClipboardDib() == null, "no picture on the clipboard");
            Check(Eval(await c.Call("test.editor.eval", new { windowId, script = $"String({figure}.classList.contains('ag-active'))" })) == "false", "no cell was opened for editing");

            if (PasteIntoWord(System.IO.Path.GetDirectoryName(path)!) is { } word)
            {
                var tables = string.Join("; ", word.tables.Select(t => $"{t.rows}x{t.columns} [{t.header}]"));
                notes.Add($"Word: {word.tables.Count} table(s) {tables}; {word.shapes} picture(s)");
                Check(word.tables.Count == 1 && word.tables[0] is { rows: 3, columns: 3, header: "Name|Score|Note" }, "Word: the copy pastes as one table of 3 rows and 3 columns with the header's texts");
            }
            else
                notes.Add("Word: not installed, not tried");

            await Task.Delay(800);
            var after = await Get(c, id);
            Check((string)after["text"]! == text, "the document's text is as it was");
            Check(await Revision(c, id) == revision, "the document's revision is as it was (no edit, nothing in its history)");
            Check((bool)after["saved"]! == (bool)before["saved"]!, "the document's saved state is as it was");

            // The context menu: on a paragraph no Copy table; in a cell Copy table, which copies the table.
            var paragraph = await ContextMenuAt(c, windowId, window, "#ag-editor-id p");
            Check(!paragraph.ContainsKey("CopyTableItem"), "the menu on a paragraph has no Copy table");
            Send(Key(0x1B, false), Key(0x1B, true));
            await Task.Delay(400);
            EmptyTheClipboard();
            var menu = await ContextMenuAt(c, windowId, window, "#ag-editor-id figure[data-role=TABLE] tbody td", "CopyTableItem");
            notes.Add("menu in a cell: " + string.Join(", ", menu.Keys));
            Check(menu.TryGetValue("CopyTableItem", out var copyTable) && copyTable.Current.IsEnabled, "the menu in a table cell has Copy table");
            ((System.Windows.Automation.InvokePattern)copyTable!.GetCurrentPattern(System.Windows.Automation.InvokePattern.Pattern)).Invoke();
            await CheckTableOnClipboard("the menu's Copy table", notes);
            await Task.Delay(500);
            Check((string)(await Get(c, id))["text"]! == text, "the document's text is as it was after the menu's copy");

            // Pasted back into the document: the table's Markdown again - its alignment, its formula and its bold - not
            // its HTML turned into Markdown.
            await PasteAtTheEnd(c, windowId, window);
            string pasted = "";
            for (var i = 0; i < 30 && Regex.Matches(pasted, Regex.Escape("$x^2$")).Count < 2; i++) { await Task.Delay(200); pasted = (string)(await Get(c, id))["text"]!; }
            notes.Add("after the paste: " + JsonConvert.SerializeObject(pasted));
            Check(Eval(await c.Call("test.editor.eval", new { windowId, script = "String(document.querySelectorAll('#ag-editor-id figure[data-role=TABLE]').length)" })) == "2", "pasting the copy back makes a second table");
            Check(Regex.Matches(pasted, Regex.Escape("$x^2$")).Count == 2 && Regex.Matches(pasted, @"\|\s*:-+:\s*\|").Count == 2 && Regex.Matches(pasted, Regex.Escape("**Score**")).Count == 2 && !pasted.Contains("<t"),
                "pasting the copy back gives the table's Markdown (alignment, formula and bold as they were)");
        }
        finally
        {
            Send(Key(0x1B, false), Key(0x1B, true));
            if (themeBefore != null) await c.Call("test.settings.set", new { windowId, name = "AppTheme", value = themeBefore });
        }
    }

    /// <summary>The table copy of CB02 on the clipboard: its Markdown as text, and HTML with the table, its header row, alignment and formatting.</summary>
    private static async Task CheckTableOnClipboard(string how, List<string> notes)
    {
        string? clip = null;
        for (var i = 0; i < 50 && clip != Cb02Table; i++) { await Task.Delay(200); clip = ClipboardText(); }
        Check(clip == Cb02Table, $"{how}: the clipboard's text is the table as the document has it (got {JsonConvert.SerializeObject(clip)})");
        var html = ClipboardHtml() ?? "";
        notes.Add($"{how}: HTML {JsonConvert.SerializeObject(html.Length > 900 ? html[..900] : html)}");
        Check(html.Contains("<table") && Regex.Matches(html, "<th[ >]").Count == 3 && Regex.Matches(html, "<tr[ >]").Count == 3,
            $"{how}: the clipboard's HTML is the table: a header row of 3 <th> and 3 rows");
        Check(html.Contains("align=\"left\"") && html.Contains("align=\"center\"") && html.Contains("align=\"right\""), $"{how}: with each column's alignment");
        Check(html.Contains("<strong>Score</strong>") && html.Contains("<em>Ann</em>") && html.Contains("<code>10</code>") && html.Contains("href=\"https://example.com\""),
            $"{how}: with the cells' bold, italic, code and link");
    }

    /// <summary>Ctrl+End, Enter and Ctrl+V in the editor: the clipboard pasted in a new paragraph at the document's end.</summary>
    private static async Task PasteAtTheEnd(Client c, string windowId, IntPtr window)
    {
        await Activate(window);
        await c.Call("test.editor.focus", new { windowId });
        await Task.Delay(200);
        Send(Key(0x11, false), Key(0x23, false), Key(0x23, true), Key(0x11, true));
        Keys((ushort)0x0D, ("ctrl", (ushort)0x56));
    }

    /// <summary>Whether the clipboard holds the format of that name (a registered one: "Rich Text Format", "HTML Format").</summary>
    private static bool ClipboardHasFormat(string name) => IsClipboardFormatAvailable(RegisterClipboardFormat(name));

    /// <summary>The names of the formats on the clipboard, for the notes (standard ones by number).</summary>
    private static List<string> ClipboardFormatNames()
    {
        var names = new List<string>();
        for (var i = 0; i < 10 && !OpenClipboard(IntPtr.Zero); i++) Thread.Sleep(50);
        try
        {
            for (uint format = EnumClipboardFormats(0); format != 0; format = EnumClipboardFormats(format))
            {
                var name = new StringBuilder(128);
                names.Add(GetClipboardFormatName(format, name, name.Capacity) > 0 ? name.ToString() : format.ToString());
            }
        }
        finally { CloseClipboard(); }
        return names;
    }

    /// <summary>
    /// Text (CF_UNICODETEXT) and, when given, an HTML fragment (CF_HTML, with the header Word reads it by) put on the
    /// clipboard as one item.
    /// </summary>
    private static void PutOnClipboard(string text, string? fragment)
    {
        for (var i = 0; i < 10 && !OpenClipboard(IntPtr.Zero); i++) Thread.Sleep(50);
        try
        {
            EmptyClipboard();
            SetClipboardBytes(13, Encoding.Unicode.GetBytes(text + "\0"));
            if (fragment != null)
            {
                const string header = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
                const string start = "<html><body><!--StartFragment-->", end = "<!--EndFragment--></body></html>";
                var headerLength = string.Format(header, 0, 0, 0, 0).Length;
                var fragmentBytes = Encoding.UTF8.GetByteCount(fragment);
                var startFragment = headerLength + start.Length;
                var endFragment = startFragment + fragmentBytes;
                var whole = string.Format(header, headerLength, endFragment + end.Length, startFragment, endFragment) + start + fragment + end;
                var bytes = Encoding.UTF8.GetBytes(whole);
                SetClipboardBytes(RegisterClipboardFormat("HTML Format"), bytes.Concat(new byte[] { 0 }).ToArray());
            }
        }
        finally { CloseClipboard(); }
    }

    private static void SetClipboardBytes(uint format, byte[] bytes)
    {
        var memory = GlobalAlloc(0x0002, (UIntPtr)bytes.Length);
        var pointer = GlobalLock(memory);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        GlobalUnlock(memory);
        if (SetClipboardData(format, memory) == IntPtr.Zero) throw new CaseFailed($"the clipboard did not take format {format}");
    }

    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] private static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClipboardFormatName(uint format, StringBuilder name, int size);
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
}
