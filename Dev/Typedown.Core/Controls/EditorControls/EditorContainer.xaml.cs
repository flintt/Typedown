using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Typedown.Core.Interfaces;
using Typedown.Core.Models;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Input;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace Typedown.Core.Controls
{
    public sealed partial class EditorContainer : UserControl
    {
        private readonly static DependencyProperty IsFindReplaceLoadProperty = DependencyProperty.Register(nameof(IsFindReplaceLoad), typeof(bool), typeof(EditorContainer), new(false));
        private bool IsFindReplaceLoad { get => (bool)GetValue(IsFindReplaceLoadProperty); set => SetValue(IsFindReplaceLoadProperty, value); }

        private readonly static DependencyProperty FindReplaceCenterPointProperty = DependencyProperty.Register(nameof(FindReplaceCenterPoint), typeof(Point), typeof(EditorContainer), new(new Point()));
        private Point FindReplaceCenterPoint { get => (Point)GetValue(FindReplaceCenterPointProperty); set => SetValue(FindReplaceCenterPointProperty, value); }

        private readonly static DependencyProperty ScrollStateProperty = DependencyProperty.Register(nameof(ScrollState), typeof(ScrollState), typeof(EditorContainer), new(new ScrollState(), (d, e) => (d as EditorContainer).OnDependencyPropertyChanged(e)));
        private ScrollState ScrollState { get => (ScrollState)GetValue(ScrollStateProperty); set => SetValue(ScrollStateProperty, value); }

        public AppViewModel ViewModel => DataContext as AppViewModel;
        public FloatViewModel Float => ViewModel?.FloatViewModel;
        public EditorViewModel Editor => ViewModel?.EditorViewModel;
        public SettingsViewModel Settings => ViewModel?.SettingsViewModel;
        public FormatViewModel Format => ViewModel?.FormatViewModel;

        private readonly CompositeDisposable disposables = new();

        public EditorContainer()
        {
            InitializeComponent();
            AddHandler(PointerMovedEvent, new PointerEventHandler(OnPointerPointerMoved), true);
            AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnPointerWheelChanged), true);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            MarkdownEditorPresenter.Content = this.GetService<IMarkdownEditor>();
            // Back in a window: the page went to the top while it had none (see EditorViewModel.EditorDetached).
            if (Editor != null && Editor.EditorDetached)
            {
                Editor.EditorDetached = false;
                Editor.RestoreScroll();
            }
            disposables.Add(Float.WhenPropertyChanged(nameof(Float.FindReplaceDialogOpen))
                .Cast<FloatViewModel.FindReplaceDialogState>()
                .Subscribe(x => UpdateFindReplaceState(x, true)));
            disposables.Add(Editor.EventCenter.GetObservable<EditorEventArgs>("OnScroll")
                .Subscribe(x => ScrollState = x.Args.ToObject<ScrollState>()));
            UpdateFindReplaceState(Float.FindReplaceDialogOpen, false);
            // The home page reads its words when it is built ({u:LocaleString}, its dates): after the language changed from
            // another window or the automation API it stayed in the old one. A new one takes its place, as it was.
            disposables.Add(Settings.WhenPropertyChanged(nameof(SettingsViewModel.Language)).Subscribe(_ =>
                _ = DispatcherQueue.RunIdleAsync(_ => RebuildHome())));
            // An edition's page over the editor; gone when another tab or file is shown (back to the writing).
            EditionHooks.OverlayChanged += OnOverlayChanged;
            disposables.Add(Disposable.Create(() => EditionHooks.OverlayChanged -= OnOverlayChanged));
            if (ViewModel?.TabsViewModel is { } tabs)
                disposables.Add(tabs.WhenPropertyChanged(nameof(TabsViewModel.ActiveTab)).Subscribe(_ => EditionHooks.CloseOverlay(ViewModel)));
            if (Editor != null)
                disposables.Add(ViewModel.FileViewModel.WhenPropertyChanged(nameof(FileViewModel.FilePath)).Subscribe(_ => EditionHooks.CloseOverlay(ViewModel)));
            ShowOverlay();
        }

        private void OnOverlayChanged(AppViewModel app)
        {
            if (app != ViewModel) return;
            if (DispatcherQueue.HasThreadAccess) ShowOverlay();
            else DispatcherQueue.TryEnqueue(ShowOverlay);
        }

        // Which container's host holds an overlay's content now: the one before this (left for the settings) lets it go.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, ContentPresenter> overlayHosts = new();

        private void ShowOverlay()
        {
            var overlay = EditionHooks.OverlayOf(ViewModel);
            if (overlay?.Content == null)
            {
                EditionOverlayHost.Content = null;
                EditionOverlayHost.Visibility = Visibility.Collapsed;
                return;
            }
            if (overlayHosts.TryGetValue(overlay.Content, out var held) && held != EditionOverlayHost && held.Content == overlay.Content) held.Content = null;
            else if ((overlay.Content as FrameworkElement)?.Parent is ContentPresenter owner && owner != EditionOverlayHost) owner.Content = null;
            overlayHosts.AddOrUpdate(overlay.Content, EditionOverlayHost);
            EditionOverlayHost.Content = overlay.Content;
            EditionOverlayHost.Visibility = Visibility.Visible;
        }

        private void RebuildHome()
        {
            if (HomeView?.Parent is not Panel parent) return;
            var at = parent.Children.IndexOf(HomeView);
            if (at < 0) return;
            var fresh = new HomePage { AllowDrop = true };
            fresh.DragEnter += OnDragEnter;
            fresh.Drop += OnDrop;
            parent.Children[at] = fresh;
            HomeView = fresh;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (Editor != null) Editor.EditorDetached = true;
            MarkdownEditorPresenter.Content = null;
            EditionOverlayHost.Content = null;
            disposables.Clear();
            Bindings?.StopTracking();
        }

        private void UpdateFindReplaceState(FloatViewModel.FindReplaceDialogState findReplaceOpen, bool useTransitions = true)
        {
            FindReplaceCenterPoint = new(ActualWidth / 2, findReplaceOpen switch
            {
                FloatViewModel.FindReplaceDialogState.Search => 32,
                FloatViewModel.FindReplaceDialogState.Replace => 52,
                _ => FindReplaceCenterPoint.Y
            });
            VisualStateManager.GoToState(this, findReplaceOpen != FloatViewModel.FindReplaceDialogState.None ? "FindReplaceVisible" : "FindReplaceCollapsed", useTransitions && Settings.AnimationEnable);
        }

        private void OnScroll(object sender, ScrollEventArgs e)
        {
            ViewModel.MarkdownEditor.PostMessage("OnScroll", new { ScrollX = HorizontalScrollBar.Value, ScrollY = VerticalScrollBar.Value });
        }

        private Visibility IsScrollBarVisibility(double maximum)
        {
            return maximum <= 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private double GetSmallChange(double fontSize, double lineHeight)
        {
            return fontSize * lineHeight;
        }

        private async void OnDragEnter(object sender, DragEventArgs e)
        {
            var deferral = e.GetDeferral();
            try
            {
                Log.Debug($"DragEnter: formats=[{string.Join(", ", e.DataView.AvailableFormats)}]");
                if (e.DataView.Contains(StandardDataFormats.StorageItems))
                {
                    var items = await e.DataView.GetStorageItemsAsync();
                    Log.Debug($"DragEnter: {items.Count} storage item(s): {string.Join(", ", items.Select(x => x.Path))}");
                    if (items.Any(x => FileTypeHelper.IsImageFile(x.Path)))
                    {
                        e.AcceptedOperation = DataPackageOperation.Link;
                        e.DragUIOverride.Caption = Locale.GetString("InsertImage");
                    }
                    else if (items.Any(x => FileTypeHelper.IsMarkdownFile(x.Path)))
                    {
                        e.AcceptedOperation = DataPackageOperation.Link;
                        e.DragUIOverride.Caption = Locale.GetString("Open");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"DragEnter failed: {ex}");
                e.AcceptedOperation = DataPackageOperation.None;
            }
            finally
            {
                deferral.Complete();
            }
        }

        private async void OnDrop(object sender, DragEventArgs e)
        {
            Log.Debug($"Drop: formats=[{string.Join(", ", e.DataView.AvailableFormats)}]");
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                try
                {
                    var items = await e.DataView.GetStorageItemsAsync();
                    // The pictures into this document first, in the drop's order; then the text files open.
                    await ViewModel.EditorViewModel.InsertLocalImagesAsync(items.Select(x => x.Path).Where(FileTypeHelper.IsImageFile).ToList());
                    foreach (var path in items.Select(x => x.Path).Where(FileTypeHelper.IsEditableTextFile))
                        ViewModel.FileViewModel.OpenFileCommand.Execute(path);
                }
                catch (Exception ex)
                {
                    // async void: nothing above it would catch.
                    Log.Debug($"Drop failed: {ex}");
                }
            }
        }

        private void OnPointerPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
            {
                HorizontalScrollBar.IndicatorMode = ScrollingIndicatorMode.TouchIndicator;
                VerticalScrollBar.IndicatorMode = ScrollingIndicatorMode.TouchIndicator;
            }
            else
            {
                HorizontalScrollBar.IndicatorMode = ScrollingIndicatorMode.MouseIndicator;
                VerticalScrollBar.IndicatorMode = ScrollingIndicatorMode.MouseIndicator;
            }
        }

        private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control))
            {
                var delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
                Settings.FontSize = Math.Max(8, Math.Min(48, Math.Round(Settings.FontSize * (1 + delta / 1200d), 1)));
            }
        }

        private void OnFormatItemClick(object sender, EventArgs e)
        {
            Flyout.Hide();
        }

        private void OnDependencyPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            if (e.Property == ScrollStateProperty)
            {
                if (e.OldValue is ScrollState oldValue && e.NewValue is ScrollState newValue && MarkdownEditorPresenter.Content is IMarkdownEditor editor)
                {
                    editor.MoveDummyRectangle(new(oldValue.ScrollX - newValue.ScrollX, oldValue.ScrollY - newValue.ScrollY));
                }
            }
        }

        public static bool IsLoadImageMenu(bool isImageFormat, JToken selection)
        {
            return isImageFormat && (selection["selectedImage"]?.HasValues ?? false);
        }

        /// <summary>The bold/italic/... row: not in reading mode, where the page refuses every format.</summary>
        // Not in source mode: its buttons did nothing there (the menu report's M02, seen in the page).
        public static bool IsLoadFormatMenu(bool formatIsEnable, bool readOnly, bool sourceCode) => formatIsEnable && !readOnly && !sourceCode;

        /// <summary>
        /// Copy table: when the last right-click was in a table (the page says so as the button goes down), in the
        /// visual editor and reading mode; source mode has no tables.
        /// </summary>
        public static Visibility TableMenuVisibility(bool inTable, bool sourceCode) => inTable && !sourceCode ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Paste as plain text: in the visual editor only (source mode has no paste-as, reading mode no paste).</summary>
        public static Visibility IsPasteAsVisible(bool sourceCode, bool readOnly, bool onGhost) => sourceCode || readOnly || onGhost ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>Cut, paste and delete: not in reading mode, nor on a deleted block review mode draws (not the document's text).</summary>
        public static Visibility EditVisibility(bool readOnly, bool onGhost) => readOnly || onGhost ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>A link's items: right-clicked on a link in the visual editor; <paramref name="editsOnly"/> (reading mode) hides the ones that change it.</summary>
        public static Visibility LinkMenuVisibility(bool onLink, bool sourceCode, bool editsOnly) => onLink && !sourceCode && !editsOnly ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>The line under the object's items: there when one of them is (flags only: x:Bind calls no function with a null argument).</summary>
        public static Visibility ObjectGroupVisibility(bool inTable, bool onLink, bool inCode, bool onMath, bool onDiagram, bool onRevision, bool sourceCode) =>
            !sourceCode && (inTable || onLink || inCode || onMath || onDiagram || onRevision) ? Visibility.Visible : Visibility.Collapsed;
    }
}
