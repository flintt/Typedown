using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.Utilities
{
    public static class ControlExtensions
    {
        // Kept by the trimmer: it is protected and reached by reflection only.
        [System.Diagnostics.CodeAnalysis.DynamicDependency("GetTemplateChild", typeof(Control))]
        public static DependencyObject GetTemplateChild(this Control control, string childName)
        {
            var method = typeof(Control).GetMethod("GetTemplateChild", BindingFlags.NonPublic | BindingFlags.Instance);
            return method.Invoke(control, new object[] { childName }) as DependencyObject;
        }
    }
}
