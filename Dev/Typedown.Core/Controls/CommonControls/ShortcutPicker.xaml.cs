using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reflection;
using Typedown.Core.Controls.SettingControls.SettingItems;
using Typedown.Core.Interfaces;
using Typedown.Core.Models;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.Controls
{
    public sealed partial class ShortcutPicker : UserControl
    {
        public static DependencyProperty ShortcutKeyProperty = DependencyProperty.Register(nameof(ShortcutKey), typeof(ShortcutKey), typeof(ShortcutPicker), new(null));
        public ShortcutKey ShortcutKey { get => (ShortcutKey)GetValue(ShortcutKeyProperty); set => SetValue(ShortcutKeyProperty, value); }

        public static DependencyProperty VerifiedProperty = DependencyProperty.Register(nameof(Verified), typeof(bool), typeof(ShortcutPicker), new(true));
        public bool Verified { get => (bool)GetValue(VerifiedProperty); set => SetValue(VerifiedProperty, value); }

        private readonly CompositeDisposable disposables = new();

        private ShortcutKey currentShortcutKey;

        private Dictionary<ShortcutKey, PropertyInfo> existShortcutKeys;

        private HashSet<VirtualKey> modifiers;

        private SettingsViewModel settings;

        // The setting being chosen for (null: none known), and what its kind allows.
        private readonly PropertyInfo property;

        private readonly bool notRecommended;

        private readonly bool digits;

        private readonly bool click;

        public ShortcutPicker(ShortcutKey currentShortcutKey, PropertyInfo property = null)
        {
            this.currentShortcutKey = currentShortcutKey;
            this.property = property;
            notRecommended = property?.GetCustomAttribute<NotRecommendedShortcutAttribute>() != null;
            digits = property?.GetCustomAttribute<DigitsShortcutAttribute>() != null;
            click = property?.GetCustomAttribute<ClickShortcutAttribute>() != null;
            ShortcutKey = currentShortcutKey;
            InitializeComponent();
            // A key people expect where it is: it may be changed, with a word that it is better not to.
            WarningText.Visibility = notRecommended ? Visibility.Visible : Visibility.Collapsed;
            ClickHint.Visibility = click ? Visibility.Visible : Visibility.Collapsed;
            RestoreDefaultButton.Visibility = property != null ? Visibility.Visible : Visibility.Collapsed;
        }

        // The key the setting comes with, put in the picker (saved with Save, as a key pressed is).
        private void OnRestoreDefaultClick(object sender, RoutedEventArgs e)
        {
            if (property == null || settings == null) return;
            ErrorMsgPanel.Visibility = Visibility.Collapsed;
            Verified = true;
            ShortcutKey = settings.DefaultOf(property) as ShortcutKey ?? new(0, 0);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            settings = this.GetService<SettingsViewModel>();
            existShortcutKeys = new();
            typeof(SettingsViewModel)
                .GetProperties()
                .Where(x => x.PropertyType == typeof(ShortcutKey))
                .Select(x => (PropertyInfo: x, ShortcutKey: x.GetValue(settings) as ShortcutKey))
                .Where(x => x.ShortcutKey != null && x.ShortcutKey != new ShortcutKey(0, 0))
                .ToList()
                // A digits shortcut holds nine keys: each of them is taken.
                .ForEach(x => { foreach (var key in x.ShortcutKey.Expand()) existShortcutKeys[key] = x.PropertyInfo; });

            modifiers = new HashSet<VirtualKey>() {
                VirtualKey.LeftControl,
                VirtualKey.RightControl,
                VirtualKey.LeftShift,
                VirtualKey.RightShift,
                VirtualKey.LeftMenu,
                VirtualKey.RightMenu,
                VirtualKey.LeftWindows,
                VirtualKey.RightWindows };
            var acc = this.GetService<IKeyboardAccelerator>();
            disposables.Add(acc.RegisterGlobal(OnKeyEvent));
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            disposables.Clear();
            Bindings?.StopTracking();
        }

        // The modifier a modifier key is.
        private static VirtualKeyModifiers ModifierOf(VirtualKey key) => key switch
        {
            VirtualKey.LeftControl or VirtualKey.RightControl or VirtualKey.Control => VirtualKeyModifiers.Control,
            VirtualKey.LeftShift or VirtualKey.RightShift or VirtualKey.Shift => VirtualKeyModifiers.Shift,
            VirtualKey.LeftMenu or VirtualKey.RightMenu or VirtualKey.Menu => VirtualKeyModifiers.Menu,
            VirtualKey.LeftWindows or VirtualKey.RightWindows => VirtualKeyModifiers.Windows,
            _ => VirtualKeyModifiers.None,
        };

        private void OnKeyEvent(object sender, KeyEventArgs args)
        {
            ErrorMsgPanel.Visibility = Visibility.Collapsed;
            // A click shortcut: the modifiers held down are what is chosen, the click stands for the key.
            if (click)
            {
                if (ModifierOf(args.Key) == VirtualKeyModifiers.None) return;
                Verified = true;
                ShortcutKey = new ShortcutKey(args.Modifiers | ModifierOf(args.Key), Models.ShortcutKey.Click);
                args.Handled = true;
                return;
            }
            var shortcutKey = new ShortcutKey(args.Modifiers, args.Key);
            var displayText = Common.GetShortcutKeyTextList(shortcutKey);
            var isDigit = args.Key >= VirtualKey.Number1 && args.Key <= VirtualKey.Number9;
            // A key with a modifier, F1-F12 or Delete; any key for a key people expect (Tab, Esc, Space); for the digits
            // 1-9, a digit with a modifier, which stands for all nine.
            var accepted = !modifiers.Contains(args.Key) && (digits
                ? args.Modifiers != 0 && isDigit
                : notRecommended || args.Modifiers != 0 || (args.Key >= VirtualKey.F1 && args.Key <= VirtualKey.F12) || args.Key == VirtualKey.Delete);
            if (accepted)
            {
                if (digits) shortcutKey = new ShortcutKey(args.Modifiers, Models.ShortcutKey.AnyDigit);
                // Taken by another setting: any of the keys it stands for.
                var owner = shortcutKey.Expand()
                    .Select(k => existShortcutKeys.TryGetValue(k, out var p) ? p : null)
                    .FirstOrDefault(p => p != null && (property != null ? p != property && !SettingsViewModel.MayShare(p.Name, property.Name) : shortcutKey != currentShortcutKey));
                if (owner != null)
                {
                    Verified = false;
                    ErrorMsgPanel.Visibility = Visibility.Visible;
                    ExistOwnerTextBlock.Text = new ShortcutSettingItemModel(settings, owner).Description;
                }
                else
                {
                    Verified = true;
                }
                ShortcutKey = shortcutKey;
                args.Handled = true;
            }
            else if (displayText.ToHashSet().Count == displayText.Count)
            {
                Verified = false;
                ShortcutKey = shortcutKey;
            }
        }

        public void ResetShortcutKey()
        {
            Verified = true;
            ErrorMsgPanel.Visibility = Visibility.Collapsed;
            ShortcutKey = new(0, 0);
        }
    }
}
