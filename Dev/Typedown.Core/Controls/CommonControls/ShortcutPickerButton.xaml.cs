using System;
using System.Linq;
using System.Reactive.Linq;
using Typedown.Core.Models;
using Typedown.Core.Utilities;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.Controls
{
    public sealed partial class ShortcutPickerButton : Button
    {
        public static DependencyProperty ShortcutKeyProperty = DependencyProperty.Register(nameof(ShortcutKey), typeof(ShortcutKey), typeof(ShortcutPickerButton), new(null));
        public ShortcutKey ShortcutKey { get => (ShortcutKey)GetValue(ShortcutKeyProperty); set => SetValue(ShortcutKeyProperty, value); }

        /// <summary>The setting this button edits (its PropertyInfo): the picker reads from it whether a key without a modifier
        /// is allowed and a warning shown (NotRecommendedShortcut), or only the modifiers of the digits 1-9 are chosen
        /// (DigitsShortcut).</summary>
        public static DependencyProperty SettingProperty = DependencyProperty.Register(nameof(Setting), typeof(object), typeof(ShortcutPickerButton), new(null));
        public object Setting { get => GetValue(SettingProperty); set => SetValue(SettingProperty, value); }

        public ShortcutPickerButton()
        {
            InitializeComponent();
        }

        private async void OnClick(object sender, RoutedEventArgs e)
        {
            var picker = new ShortcutPicker(ShortcutKey, Setting as System.Reflection.PropertyInfo);
            var dialog = new AppContentDialog
            {
                XamlRoot = XamlRoot,
                Title = Locale.GetDialogString("SetShortcutKeyTitle"),
                Content = picker,
                PrimaryButtonText = Locale.GetString("Save"),
                SecondaryButtonText = Locale.GetString("Clear"),
                CloseButtonText = Locale.GetString("Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                IsSecondaryButtonEnabled = ShortcutKey != null && ShortcutKey != new ShortcutKey(0, 0),
            };
            picker.Binding(new(nameof(picker.ShortcutKey))).Cast<ShortcutKey>().Subscribe(_ => OnPickerShortcutKeyChanged(dialog));
            dialog.PrimaryButtonClick += OnDialogPrimaryButtonClick;
            dialog.SecondaryButtonClick += OnDialogSecondaryButtonClick;
            await dialog.ShowAsync();
        }

        private void OnDialogPrimaryButtonClick(AppContentDialog sender, AppContentDialogButtonClickEventArgs args)
        {
            var picker = sender.Content as ShortcutPicker;
            if (picker.Verified || picker.ShortcutKey == new ShortcutKey(0, 0))
                ShortcutKey = picker.ShortcutKey;
            else
                args.Cancel = true;
        }

        private void OnDialogSecondaryButtonClick(AppContentDialog sender, AppContentDialogButtonClickEventArgs args)
        {
            var picker = sender.Content as ShortcutPicker;
            picker.ResetShortcutKey();
            args.Cancel = true;
        }

        private void OnPickerShortcutKeyChanged(AppContentDialog dialog)
        {
            var picker = dialog.Content as ShortcutPicker;
            dialog.IsPrimaryButtonEnabled = picker.Verified;
            dialog.IsSecondaryButtonEnabled = picker.ShortcutKey != null && picker.ShortcutKey != new ShortcutKey(0, 0);
        }

        public static bool HasShortcutKey(ShortcutKey key)
        {
            return key != null && key.Key != Windows.System.VirtualKey.None;
        }

        public static bool HasShortcutKeyReverse(ShortcutKey key)
        {
            return !HasShortcutKey(key);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {

        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Bindings?.StopTracking();
        }
    }
}
