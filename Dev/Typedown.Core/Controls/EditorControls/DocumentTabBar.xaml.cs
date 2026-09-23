using System.Reactive.Disposables;
using Typedown.Core.Models;
using Typedown.Core.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using muxc = Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.Controls
{
    /// <summary>Tab strip above the editor; one tab per open document (see <see cref="TabsViewModel"/>).</summary>
    public sealed partial class DocumentTabBar : UserControl
    {
        public AppViewModel ViewModel => DataContext as AppViewModel;

        public TabsViewModel Tabs => ViewModel?.TabsViewModel;

        private readonly CompositeDisposable disposables = new();

        public DocumentTabBar()
        {
            InitializeComponent();
            // handledEventsToo: once there are more tabs than fit, the strip's own scroll viewer takes the
            // wheel and marks it handled, so a plain handler never sees it and the wheel only slid the strip
            // sideways instead of moving between tabs.
            AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnPointerWheelChanged), true);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Bindings.Update();
        }

        /// <summary>
        /// The wheel over the tab strip moves between tabs: up goes left, down goes right. The strip scrolls
        /// itself when there are more tabs than fit, which is never what the wheel is wanted for here.
        /// </summary>
        private void OnPointerWheelChanged(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (Tabs == null) return;
            var delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
            if (delta == 0) return;
            e.Handled = true;
            (delta > 0 ? Tabs.PreviousTabCommand : Tabs.NextTabCommand).Execute(default);
            // The strip may have scrolled itself before this ran; following the selection puts it right.
            TabView.SelectedItem = Tabs.ActiveTab;
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Tabs == null) return;
            if (TabView.SelectedItem is DocumentTab tab && tab != Tabs.ActiveTab)
                Tabs.SwitchTabCommand.Execute(tab);
        }

        private void OnAddTabButtonClick(muxc.TabView sender, object args)
        {
            ViewModel?.FileViewModel.NewFileCommand.Execute(default);
        }

        private void OnTabCloseRequested(muxc.TabView sender, muxc.TabViewTabCloseRequestedEventArgs args)
        {
            if (args.Item is DocumentTab tab)
                Tabs?.CloseTabCommand.Execute(tab);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            disposables.Clear();
            Bindings?.StopTracking();
        }
    }
}
