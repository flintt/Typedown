using System;
using Windows.System;

namespace Typedown.Core.Models
{
    public record ShortcutKey(VirtualKeyModifiers Modifiers, VirtualKey Key)
    {
        /// <summary>
        /// The key of a shortcut that stands for nine: the digits 1 to 9 with these modifiers (switch to a tab by its
        /// place). 0xE8 is a key code Windows leaves unassigned, so no key pressed is ever taken for it.
        /// </summary>
        public const VirtualKey AnyDigit = (VirtualKey)0xE8;

        /// <summary>
        /// The "key" of a shortcut that is a mouse click with modifiers held (open a link): 0xE9, unassigned too. Only
        /// its modifiers are chosen.
        /// </summary>
        public const VirtualKey Click = (VirtualKey)0xE9;

        /// <summary>The nine keys a digits shortcut stands for, 1 to 9; the one key otherwise.</summary>
        public ShortcutKey[] Expand()
        {
            if (Key != AnyDigit) return new[] { this };
            var keys = new ShortcutKey[9];
            for (var n = 1; n <= 9; n++) keys[n - 1] = new(Modifiers, VirtualKey.Number0 + n);
            return keys;
        }

        public override string ToString()
        {
            return base.ToString();
        }
    }

    /// <summary>
    /// A shortcut setting for a key people expect where it is (Tab to indent, Esc to close, Shift+Enter for a line
    /// break): it can be changed, but the settings page says it is better left alone, and it may be a key without
    /// a modifier.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class NotRecommendedShortcutAttribute : Attribute { }

    /// <summary>
    /// A shortcut setting for nine keys, the digits 1 to 9: only its modifiers are chosen (its key is
    /// <see cref="ShortcutKey.AnyDigit"/>).
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class DigitsShortcutAttribute : Attribute { }

    /// <summary>
    /// A shortcut setting for a click with modifiers held: only its modifiers are chosen, at least one (its key is
    /// <see cref="ShortcutKey.Click"/>).
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class ClickShortcutAttribute : Attribute { }
}
