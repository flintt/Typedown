using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Typedown.Core.Controls;

namespace Typedown.Core.Utilities
{
    /// <summary>
    /// What the export and the image upload configurations' pages and "add" dialogs share (the modularity audit's M13): a
    /// name that may not be empty, the configuration read (a database that cannot be read leaves the page empty, logged),
    /// and saved as its page is left (a save that fails is said). Each page keeps its own service, fields and controls.
    /// </summary>
    public static class ConfigEditor
    {
        /// <summary>The "add" dialog's primary button refused while the name is empty, with the reason shown.</summary>
        public static void RequireName(AppContentDialog dialog, Func<string> name, Action<string> showError)
        {
            dialog.PrimaryButtonClick += (s, e) =>
            {
                if (!string.IsNullOrEmpty(name())) return;
                e.Cancel = true;
                showError(Locale.GetString("NameCannotBeEmpty"));
            };
        }

        /// <summary>The configuration, or null when it could not be read (logged as <paramref name="what"/>).</summary>
        public static async Task<T> LoadAsync<T>(Func<Task<T>> load, string what) where T : class
        {
            try
            {
                return await load();
            }
            catch (Exception ex)
            {
                // The database could not be read: the page stays empty rather than taking the window down.
                Log.WriteLocal(what, ex.ToString());
                return null;
            }
        }

        /// <summary>
        /// Saved once the page is gone, on the window's queue; one that could not be saved (the database locked) is said in
        /// the window's own root - the edits were lost silently.
        /// </summary>
        public static void SaveOnLeave<T>(DispatcherQueue queue, XamlRoot root, T config, Func<T, Task<bool>> save, Func<T, string> name) where T : class
        {
            if (config == null) return;
            _ = queue.RunIdleAsync(async () =>
            {
                if (!await save(config) && root != null)
                    await AppContentDialog.Create(Locale.GetDialogString("SaveErrorTitle"), name(config), Locale.GetString("Ok")).ShowAsync(root);
            });
        }
    }
}
