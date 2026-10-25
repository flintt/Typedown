using System;
using Windows.System;

namespace Typedown.Core.Models
{
    public class KeyEventArgs : EventArgs
    {
        public bool Handled { get; set; }

        public VirtualKey Key { get; }

        public VirtualKeyModifiers Modifiers { get; }

        /// <summary>The key held down and sent again by the system (auto-repeat), not pressed anew.</summary>
        public bool IsRepeat { get; init; }

        public KeyEventArgs(VirtualKey key, VirtualKeyModifiers modifiers)
        {
            Key = key;
            Modifiers = modifiers;
        }
    }
}
