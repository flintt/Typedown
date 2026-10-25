using System;
using System.ComponentModel;
using Windows.Foundation;
using Microsoft.UI.Xaml.Shapes;

namespace Typedown.Core.Interfaces
{
    public interface IMarkdownEditor : IDisposable, INotifyPropertyChanged
    {
        bool PostMessage(string name, object arg);

        Rectangle GetDummyRectangle(Rect rect);

        Rectangle MoveDummyRectangle(Point offset);

        bool IsEditorLoadFailed { get; }

        bool IsEditorLoaded { get; }

        /// <summary>The keyboard to the editor page: the control focused, and the page given the keys.</summary>
        void FocusEditor();

        /// <summary>
        /// As FocusEditor, but with the keyboard in the web view already, only the page's editor is given it: moving the
        /// focus out and back in made the input method drop the characters typed straight after (E2E TL01, a new tab).
        /// </summary>
        void FocusEditorInPlace();

        /// <summary>
        /// The page is printed (true) or no longer (false): around the page the print preview shows the web view's own
        /// colour, which is the theme's page colour so a theme change does not flash - white while printing.
        /// </summary>
        void SetPrinting(bool printing);
    }
}
