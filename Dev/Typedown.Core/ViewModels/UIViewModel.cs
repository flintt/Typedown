using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using Typedown.Core.Services;
using Typedown.Core.Utilities;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;

namespace Typedown.Core.ViewModels
{
    public sealed partial class UIViewModel : INotifyPropertyChanged, IDisposable
    {
        public IServiceProvider ServiceProvider { get; }

        public AppViewModel AppViewModel => ServiceProvider.GetService<AppViewModel>();

        public EditorViewModel EditorViewModel => ServiceProvider.GetService<EditorViewModel>();

        public FileViewModel FileViewModel => ServiceProvider.GetService<FileViewModel>();

        public SettingsViewModel SettingsViewModel => ServiceProvider.GetService<SettingsViewModel>();

        public RemoteInvoke RemoteInvoke => ServiceProvider.GetService<RemoteInvoke>();

        public string MainWindowTitle { get; private set; }

        public ElementTheme ActualTheme { get; private set; }

        public double CaptionHeight { get; set; } = 32;

        public bool IsPrintPreviewOpen { get; private set; }


        /// <summary>Folder-wide search pane in the side bar (Edit → Find → Search in folder, Ctrl+Shift+F).</summary>
        public bool FolderSearchOpen { get; set; }

        public Command<Unit> SearchInFolderCommand { get; } = new();

        /// <summary>Borderless full-screen window without the caption/menu bar (F11).</summary>

        public bool IsFullScreen { get; set; }

        public object PrintPreviewContent { get; private set; }

        public string PrintPreviewTitle { get; private set; }

        private readonly CompositeDisposable disposables = new();

        private readonly UISettings uiSettings = new();

        private readonly CoreDispatcher dispatcher;

        public UIViewModel(IServiceProvider serviceProvider)
        {
            dispatcher = CoreApplication.GetCurrentView().CoreWindow.Dispatcher;
            ServiceProvider = serviceProvider;
            SearchInFolderCommand.OnExecute.Subscribe(_ =>
            {
                SettingsViewModel.SidePaneOpen = true;
                FolderSearchOpen = true;
            });
            disposables.Add(RemoteInvoke.Handle<JToken, object>("GetStringResources", GetStringResources));
            _ = dispatcher.RunIdleAsync(() => InitializeBinding());
        }

        private void InitializeBinding()
        {
            if (disposables.IsDisposed)
                return;
            disposables.Add(EditorViewModel.WhenPropertyChanged(nameof(EditorViewModel.DisplaySaved)).Subscribe(_ => UpdateTitle()));
            disposables.Add(FileViewModel.WhenPropertyChanged(nameof(FileViewModel.FileName)).Subscribe(_ => UpdateTitle()));
            disposables.Add(SettingsViewModel.WhenPropertyChanged(nameof(SettingsViewModel.ReadOnly)).Subscribe(_ => UpdateTitle()));
            // The title carries a translated word now, so it has to be rebuilt when the language changes —
            // otherwise it keeps the old one while the menus around it have already changed.
            disposables.Add(SettingsViewModel.WhenPropertyChanged(nameof(SettingsViewModel.Language)).Subscribe(_ => UpdateTitle()));
            disposables.Add(Observable.FromEventPattern(uiSettings, nameof(uiSettings.ColorValuesChanged))
                .Merge(SettingsViewModel.WhenPropertyChanged(nameof(SettingsViewModel.AppTheme)))
                .Merge(SettingsViewModel.WhenPropertyChanged(nameof(SettingsViewModel.CustomTheme)))
                .Subscribe(_ => UpdateActualTheme()));
            UpdateTitle();
            UpdateActualTheme();
        }

        private object GetStringResources(JToken args)
        {
            try
            {
                return args["names"].ToObject<List<string>>().ToDictionary(x => x, x => Locale.GetString(x));
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }

        private void UpdateActualTheme()
        {
            // Not at idle priority: with a large document rendering or a folder being walked, an idle callback
            // waits for the thread to go quiet, and the window sits in the old theme until it does.
            _ = dispatcher?.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                try
                {
                    var theme = SettingsViewModel.EffectiveTheme;
                    if (theme == Enums.AppTheme.Default)
                        ActualTheme = Application.Current.RequestedTheme == ApplicationTheme.Light ? ElementTheme.Light : ElementTheme.Dark;
                    else
                        ActualTheme = theme == Enums.AppTheme.Light ? ElementTheme.Light : ElementTheme.Dark;
                }
                catch
                {
                    // Ignore
                }
            });
        }

        private void UpdateTitle()
        {
            try
            {
                var title = new StringBuilder();
                if (!AppViewModel.EditorViewModel.DisplaySaved)
                    title.Append('*');
                if (AppViewModel.FileViewModel.FileName != null)
                    title.Append(AppViewModel.FileViewModel.FileName + " - ");
                title.Append(Config.AppName);
                // Reading mode swallows every keystroke, and without a word about it that looks like the editor
                // has stopped responding. The status bar says so too, but it can be switched off — the title
                // cannot, and it is what the taskbar shows.
                if (SettingsViewModel.ReadOnly)
                    title.Append(" \u00b7 " + Locale.GetString("ReadOnlyMode"));
                MainWindowTitle = title.ToString();
            }
            catch
            {
                // Ignore
            }
        }

        public void ShowPrintPreview(object content, string title = null)
        {
            ClosePrintPreview();
            PrintPreviewTitle = title;
            PrintPreviewContent = content;
            IsPrintPreviewOpen = true;
        }

        public void ClosePrintPreview()
        {
            IsPrintPreviewOpen = false;
            var oldContent = PrintPreviewContent;
            PrintPreviewContent = null;
            PrintPreviewTitle = null;
            (oldContent as IDisposable)?.Dispose();
        }

        public void Dispose()
        {
            ClosePrintPreview();
            disposables.Dispose();
        }
    }
}
