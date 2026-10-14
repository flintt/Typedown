using System.IO;

namespace Typedown.Core.Services
{
    /// <summary>The app's favourites (favorites.json beside session.json): one store for every window.</summary>
    public static class Favorites
    {
        private static FavoritesStore shared;

        private static readonly object gate = new();

        public static FavoritesStore Shared
        {
            get
            {
                lock (gate)
                {
                    if (shared == null)
                    {
                        shared = new FavoritesStore(Path.Combine(Config.GetLocalFolderPath(), "favorites.json"));
                        _ = shared.LoadAsync();
                    }
                    return shared;
                }
            }
        }
    }
}
