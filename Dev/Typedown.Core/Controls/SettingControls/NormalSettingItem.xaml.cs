using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Typedown.Core.Controls
{
    [ContentProperty(Name = nameof(Action))]
    public sealed partial class NormalSettingItem : UserControl
    {
        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(object), typeof(NormalSettingItem), null);
        public object Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

        public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(object), typeof(NormalSettingItem), null);
        public object Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

        public static readonly DependencyProperty ActionProperty = DependencyProperty.Register(nameof(Action), typeof(object), typeof(NormalSettingItem), null);
        public object Action { get => GetValue(ActionProperty); set => SetValue(ActionProperty, value); }

        public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(IconElement), typeof(NormalSettingItem), null);
        public IconElement Icon { get => (IconElement)GetValue(IconProperty); set => SetValue(IconProperty, value); }

        public static readonly DependencyProperty HorizontalActionAlignmentProperty = DependencyProperty.Register(nameof(HorizontalActionAlignment), typeof(HorizontalAlignment), typeof(NormalSettingItem),
            new(HorizontalAlignment.Right, (d, _) => ((NormalSettingItem)d).UpdateColumns()));
        public HorizontalAlignment HorizontalActionAlignment { get => (HorizontalAlignment)GetValue(HorizontalActionAlignmentProperty); set => SetValue(HorizontalActionAlignmentProperty, value); }

        public NormalSettingItem()
        {
            InitializeComponent();
            UpdateColumns();
        }

        // A switch or a list (the action at the right): the text takes the room left and wraps, the action its own size.
        // The text's column was sized to the text, never wrapped, and a long description pushed the switch out of the
        // window (Settings > Editor, "Join the lines of a paragraph in reading mode"). A stretched action (a box filling
        // the row) keeps the room after the text.
        private void UpdateColumns()
        {
            if (TextColumn == null || ActionColumn == null) return;
            var stretch = HorizontalActionAlignment == HorizontalAlignment.Stretch;
            TextColumn.Width = stretch ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            ActionColumn.Width = stretch ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Bindings?.StopTracking();
        }
    }
}
