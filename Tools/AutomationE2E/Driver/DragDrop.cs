using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// DR01: files dragged from Explorer onto the editor, as Explorer does it - an OLE drag of a file list (CF_HDROP) from
/// another process, with the mouse: a Markdown file opens, a picture goes into the document. The XAML Islands host
/// needed a drop target of its own for this (its XAML saw such drops empty); IN01 inserts pictures without a drop.
/// </summary>
internal static partial class Program
{
    private static async Task DR01(List<string> notes)
    {
        using var c = await Session("e2e DR01");
        var id = await Open(c, Fixture("dr01.md", "# DR01\n\nText.\n"));
        var windowId = await WindowIdOf(c, id);
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        var at = await c.Call("test.editor.screenPoint", new { windowId, x = 300, y = 200 });
        var target = ((int)at["x"]!, (int)at["y"]!);

        var markdown = Fixture("dr01-dropped.md", "# Dropped\n\nfrom Explorer\n");
        await DragFiles(new[] { markdown }, target);
        string? opened = null;
        for (var i = 0; i < 50 && opened == null; i++)
        {
            await Task.Delay(100);
            opened = ((JArray)(await c.Call("document.list", new { windowId }))["documents"]!)
                .FirstOrDefault(d => string.Equals((string?)d["path"], markdown, StringComparison.OrdinalIgnoreCase))?["documentId"]?.ToString();
        }
        Check(opened != null, "a Markdown file dropped on the editor opens");

        // The picture into the document the window shows (the one just opened).
        var png = Path.Combine(Path.GetDirectoryName(markdown)!, "dr01-picture.png");
        File.WriteAllBytes(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAABAAAAAICAIAAAB/FOjAAAAAE0lEQVR4nGPQqDhBEmIY1UALDQCJj7QBup2ubwAAAABJRU5ErkJggg=="));
        await Activate(await WindowOf(windowId, c));
        await DragFiles(new[] { png }, target);
        string text = "";
        for (var i = 0; i < 50 && !text.Contains("dr01-picture"); i++)
        {
            await Task.Delay(100);
            text = (string?)(await Get(c, opened!))["text"] ?? "";
        }
        notes.Add("after the picture: " + text.Replace("\n", "\\n"));
        Check(text.Contains("dr01-picture"), "a picture dropped on the editor goes into the document");
    }

    /// <summary>
    /// An OLE drag of files from this process to a point on screen: started with the left button down over a small
    /// window of the driver's, moved to the point in steps (the target sees DragEnter and DragOver), released there.
    /// </summary>
    private static async Task DragFiles(string[] files, (int x, int y) to)
    {
        var started = new TaskCompletionSource<bool>();
        var done = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            using var form = new System.Windows.Forms.Form
            {
                FormBorderStyle = System.Windows.Forms.FormBorderStyle.None, TopMost = true, ShowInTaskbar = false,
                StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(20, 20), Size = new System.Drawing.Size(60, 60),
            };
            form.MouseDown += (_, _) =>
            {
                started.TrySetResult(true);
                var data = new System.Windows.Forms.DataObject(System.Windows.Forms.DataFormats.FileDrop, files);
                form.DoDragDrop(data, System.Windows.Forms.DragDropEffects.Copy | System.Windows.Forms.DragDropEffects.Link | System.Windows.Forms.DragDropEffects.Move);
                done.TrySetResult(true);
                form.Close();
            };
            form.Shown += (_, _) => form.Activate();
            System.Windows.Forms.Application.Run(form);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await Task.Delay(600);
        SetCursorPos(50, 50);
        await Task.Delay(100);
        Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = 0x0002 } } });
        if (await Task.WhenAny(started.Task, Task.Delay(3000)) != started.Task) throw new CaseFailed("the drag did not start");
        await Task.Delay(200);
        // To the point in steps, a real move each (OLE follows the mouse, not the cursor's position).
        for (var i = 1; i <= 20; i++)
        {
            SetCursorPos(50 + (to.x - 50) * i / 20, 50 + (to.y - 50) * i / 20);
            Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dx = 0, dy = 0, dwFlags = 0x0001 } } });
            await Task.Delay(40);
        }
        await Task.Delay(400);
        Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = 0x0004 } } });
        if (await Task.WhenAny(done.Task, Task.Delay(5000)) != done.Task) throw new CaseFailed("the drag did not end");
        thread.Join(2000);
    }
}
