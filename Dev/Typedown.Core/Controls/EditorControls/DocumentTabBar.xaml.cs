using System.Reactive.Disposables;
using Typedown.Core.Models;
using Typedown.Core.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
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
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Bindings.Update();
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
