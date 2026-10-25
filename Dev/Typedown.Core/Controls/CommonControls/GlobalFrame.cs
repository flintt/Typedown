using System.Linq;
using System.Reactive.Disposables;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.Controls
{
    public class GlobalFrame : Frame
    {
        private readonly CompositeDisposable disposables = new();

        public GlobalFrame()
        {
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            Navigated += OnNavigated;
        }

        private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            var viewModel = this.GetService<AppViewModel>();
            viewModel.FrameStack = viewModel.FrameStack.Append(this).ToList();
            disposables.Add(Disposable.Create(() => viewModel.FrameStack = viewModel.FrameStack.Where(x => x != this).ToList()));
        }

        private void OnUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            disposables.Dispose();
        }

        private void OnNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            var viewModel = this.GetService<AppViewModel>();
            if (viewModel != null)
                viewModel.GoBackCommand.IsExecutable = viewModel.FrameStack.Any(x => x.CanGoBack);
        }
    }
}
