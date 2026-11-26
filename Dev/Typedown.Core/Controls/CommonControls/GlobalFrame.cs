using System.Linq;
using System.Reactive.Disposables;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.Controls
{
    public class GlobalFrame : Frame
    {
        // Taken out of the stack on Unloaded, put back on a Loaded after it: a CompositeDisposable disposed once disposes
        // what is added to it at once, so a frame loaded again left the stack as it came in (no back button after it).
        private readonly SerialDisposable inStack = new();

        public GlobalFrame()
        {
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            Navigated += OnNavigated;
        }

        private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            var viewModel = this.GetService<AppViewModel>();
            if (viewModel == null) return;
            if (!viewModel.FrameStack.Contains(this)) viewModel.FrameStack = viewModel.FrameStack.Append(this).ToList();
            inStack.Disposable = Disposable.Create(() =>
            {
                viewModel.FrameStack = viewModel.FrameStack.Where(x => x != this).ToList();
                // Told now, not at the next navigation: the settings' own frame (sections behind it) left with the page,
                // and the editor page's back button stayed until something navigated again.
                viewModel.GoBackCommand.IsExecutable = viewModel.FrameStack.Any(x => x.CanGoBack);
            });
        }

        // Always out on Unloaded: IsLoaded is still true while it is sent, and an earlier version that kept a frame
        // "still loaded" in the stack left the settings' frame there (E2E BK01, w10 job 763: the back button on the
        // editor page).
        private void OnUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => inStack.Disposable = null;

        private void OnNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            var viewModel = this.GetService<AppViewModel>();
            if (viewModel != null)
                viewModel.GoBackCommand.IsExecutable = viewModel.FrameStack.Any(x => x.CanGoBack);
        }
    }
}
