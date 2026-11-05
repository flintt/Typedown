using System;
using System.Collections.Generic;
using System.Linq;

namespace Typedown.Core.Utilities
{
    /// <summary>
    /// Whether a focus a click brings goes where the click was. A click on what takes no focus (a setting's title, the space
    /// between rows) with nothing focused made WinUI give the focus to the first setting of the page, and the page scrolled up
    /// to it (a reader, 2026-11-05): such a focus is kept back; one going to what was clicked, or to what holds it (a switch
    /// clicked on its knob or its On/Off text), or within the control of the row clicked (a number box's spin buttons and
    /// its text) goes through. Over any tree by its parent function, so it is tested without a window.
    /// </summary>
    public static class FocusRule
    {
        /// <summary>
        /// <paramref name="under"/>: what is under the pointer, topmost first. <paramref name="actionOf"/>: the control of
        /// the setting row an element is in, or null.
        /// </summary>
        public static bool FollowsClick<T>(T next, IReadOnlyList<T> under, Func<T, T> parent, Func<T, T> actionOf) where T : class
        {
            if (under.Count == 0) return true;
            if (under.Any(element => IsWithin(element, next, parent))) return true;
            return actionOf(under[0]) is { } action && IsWithin(next, action, parent);
        }

        /// <summary>Whether element is container or below it.</summary>
        public static bool IsWithin<T>(T element, T container, Func<T, T> parent) where T : class
        {
            for (var node = element; node != null; node = parent(node))
                if (ReferenceEquals(node, container)) return true;
            return false;
        }
    }
}
