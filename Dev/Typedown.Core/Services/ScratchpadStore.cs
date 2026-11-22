using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Typedown.Core.Utilities;

namespace Typedown.Core.Services
{
    /// <summary>
    /// The scratchpad's text (the user's 33, 2026-11-22): one for the whole app, every window and tab, in scratchpad.json
    /// beside session.json - not in the settings (Restore defaults must not take it). Written half a second after the last
    /// change (a write a key was too many), and at once when asked (<see cref="FlushAsync"/>: a window closing, the app
    /// ending). <see cref="Changed"/> is raised on the thread that made the change, with who made it.
    /// </summary>
    public sealed class ScratchpadStore
    {
        public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

        private readonly string file;

        private readonly object gate = new();

        private string text;

        private bool dirty;

        private Timer timer;

        private SnapshotWriter writer;
        private SnapshotWriter Writer => LazyInitializer.EnsureInitialized(ref writer, () => new SnapshotWriter(file, "scratchpad"));

        public ScratchpadStore(string file) => this.file = file;

        /// <summary>The text changed; the argument is who changed it (a window's text box, or null).</summary>
        public event Action<object> Changed;

        /// <summary>The text, read from the file the first time (it is small).</summary>
        public string Text
        {
            get { lock (gate) return EnsureLoaded(); }
        }

        private string EnsureLoaded()
        {
            if (text != null) return text;
            text = Read();
            return text;
        }

        private string Read()
        {
            try
            {
                if (!File.Exists(file)) return "";
                return JsonConvert.DeserializeObject<Stored>(File.ReadAllText(file))?.Text ?? "";
            }
            catch (Exception ex)
            {
                // A file that cannot be read is kept aside, not overwritten by the next change.
                Log.Debug($"scratchpad: {file} could not be read ({ex.Message}); kept as .bad");
                try { File.Copy(file, file + ".bad", true); } catch { }
                return "";
            }
        }

        private sealed class Stored
        {
            [JsonProperty("version")]
            public int Version { get; set; } = 1;

            [JsonProperty("text")]
            public string Text { get; set; } = "";
        }

        /// <summary>The text set (typed in a window's text box: <paramref name="source"/> is that box).</summary>
        public void SetText(string value, object source = null)
        {
            value ??= "";
            lock (gate)
            {
                if (EnsureLoaded() == value) return;
                text = value;
                SaveSoon();
            }
            RaiseChanged(source);
        }

        /// <summary>Added at the end, a blank line after what is there (the editor's Put in Scratchpad).</summary>
        public void Append(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            lock (gate)
            {
                var now = EnsureLoaded();
                var trimmed = now.TrimEnd('\r', '\n', ' ', '\t');
                text = trimmed.Length == 0 ? value : trimmed + "\n\n" + value;
                SaveSoon();
            }
            RaiseChanged(null);
        }

        /// <summary>Done when the text is on disk (a change waiting for its half second written now).</summary>
        public Task FlushAsync()
        {
            lock (gate)
            {
                if (dirty) SaveNow();
                return Writer.FlushAsync();
            }
        }

        // Under the lock.
        private void SaveSoon()
        {
            dirty = true;
            timer ??= new Timer(_ => { lock (gate) if (dirty) SaveNow(); });
            timer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }

        // Under the lock.
        private void SaveNow()
        {
            dirty = false;
            timer?.Change(Timeout.Infinite, Timeout.Infinite);
            Writer.Write(JsonConvert.SerializeObject(new Stored { Text = text ?? "" }, Formatting.Indented));
        }

        private void RaiseChanged(object source)
        {
            try { Changed?.Invoke(source); }
            catch (Exception ex) { Log.Debug($"scratchpad: Changed: {ex.Message}"); }
        }
    }
}
