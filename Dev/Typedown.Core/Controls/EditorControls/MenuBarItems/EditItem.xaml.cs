using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using Typedown.Core.Interfaces;
using Typedown.Core.Models;
using Typedown.Core.Services;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Key = Windows.System.VirtualKey;
using Mod = Windows.System.VirtualKeyModifiers;

namespace Typedown.Core.Controls.EditorControls.MenuBarItems
{
    public sealed partial class EditItem : MenuBarItemBase
    {
        public EditorViewModel Editor => ViewModel?.EditorViewModel;

        public FloatViewModel Float => ViewModel?.FloatViewModel;

        public FloatViewModel.FindReplaceDialogState OpenSearch => FloatViewModel.FindReplaceDialogState.Search;
        public FloatViewModel.FindReplaceDialogState OpenReplace => FloatViewModel.FindReplaceDialogState.Replace;

        private readonly CompositeDisposable disposables = new();

        public AccessHistory FileHistory => this.GetService<AccessHistory>();

        public HashSet<ShortcutKey> handledKey;

        public EditItem()
        {
            InitializeComponent();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var acc = this.GetService<IKeyboardAccelerator>();
            // The keys the web view must not act on itself while the editor has the focus: its own undo, select all and
            // clipboard bypass the document's history (or select the whole page). Both the keys these commands have
            // now (Settings) and the ones they had before (a key given to another does nothing in the page, rather
            // than the browser's own undo); the commands themselves answer their keys as registered below.
            disposables.Add(acc.RegisterGlobal((s, e) =>
            {
                handledKey ??= new HashSet<Models.ShortcutKey>(new Models.ShortcutKey[]
                {
                    new(Mod.Control, Key.Z),
                    new(Mod.Control, Key.Y),
                    new(Mod.Control, Key.X),
                    new(Mod.Control, Key.C),
                    new(Mod.Control, Key.V),
                    new(Mod.Control, Key.A),
                    Settings?.ShortcutUndo,
                    Settings?.ShortcutRedo,
                    Settings?.ShortcutCut,
                    Settings?.ShortcutCopy,
                    Settings?.ShortcutPaste,
                    Settings?.ShortcutSelectAll,
                }.Where(k => k != null && k.Key != Key.None));
                if (handledKey.Contains(new(e.Modifiers, e.Key)) && !VimWants(e))
                {
                    if (EditorHasFocus())
                        e.Handled = true;
                }
            }));
        }

        protected override void OnRegisterShortcut()
        {
            RegisterEditorShortcut(Settings.ShortcutUndo, UndoItem);
            RegisterEditorShortcut(Settings.ShortcutRedo, RedoItem);
            RegisterEditorShortcut(Settings.ShortcutCut, CutItem);
            RegisterEditorShortcut(Settings.ShortcutCopy, CopyItem);
            RegisterEditorShortcut(Settings.ShortcutPaste, PasteItem);
            RegisterEditorShortcut(Settings.ShortcutCopyAsPlainText, CopyAsPlainTextItem);
            RegisterEditorShortcut(Settings.ShortcutCopyAsMarkdown, CopyAsMarkdownItem);
            RegisterEditorShortcut(Settings.ShortcutCopyAsHTMLCode, CopyAsHTMLCodeItem);
            RegisterEditorShortcut(Settings.ShortcutPasteAsPlainText, PasteAsPlainTextItem);
            // RegisterEditorShortcut(Settings.ShortcutDelete, DeleteItem);
            RegisterEditorShortcut(Settings.ShortcutSelectAll, SelectAllItem);
            RegisterWindowShortcut(Settings.ShortcutFind, FindItem);
            RegisterWindowShortcut(Settings.ShortcutFindNext, FindNextItem);
            RegisterWindowShortcut(Settings.ShortcutFindPrevious, FindPreviousItem);
            RegisterWindowShortcut(Settings.ShortcutReplace, ReplaceItem);
            RegisterWindowShortcut(Settings.ShortcutReplaceOne, ReplaceOneItem);
            RegisterWindowShortcut(Settings.ShortcutReplaceAll, ReplaceAllItem);
            RegisterWindowShortcut(Settings.ShortcutSearchInFolder, SearchInFolderItem);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            disposables.Clear();
            Bindings?.StopTracking();
        }
    }
}
