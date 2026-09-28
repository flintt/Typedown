using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Threading.Tasks;
using Typedown.Core.Utilities;

namespace Typedown.Core.Services
{
    /// <summary>
    /// Owns the in-memory settings object and persists coherent snapshots in order. A burst of property changes
    /// is folded into the newest pending snapshot, so two writes never race and an older snapshot can never land
    /// after a newer one.
    /// </summary>
    public sealed class JsonSettingsStore
    {
        private readonly object sync = new();
        private readonly string path;
        private readonly Func<string, string, Task> writer;
        private readonly Action<Exception> onWriteError;
        private JObject values;
        private bool writeRequested;
        private bool writerRunning;
        private Task writerTask = Task.CompletedTask;

        public Exception LastWriteError { get; private set; }

        public JsonSettingsStore(string path, Func<string, string, Task> writer = null, Action<Exception> onWriteError = null)
        {
            this.path = path ?? throw new ArgumentNullException(nameof(path));
            this.writer = writer ?? ((target, json) => SafeFile.WriteAllTextAtomicAsync(target, json));
            this.onWriteError = onWriteError;
            values = Load(path);
        }

        private static JObject Load(string path)
        {
            try
            {
                return JObject.Parse(File.ReadAllText(path));
            }
            catch
            {
                // A missing or damaged file must not prevent startup. The next successful setting change replaces
                // it atomically with a valid document; unknown values in a valid old file remain in the JObject.
                return new JObject();
            }
        }

        public T Get<T>(string name, T defaultValue = default)
        {
            lock (sync)
            {
                try
                {
                    var token = values[name];
                    if (token == null || token.Type == JTokenType.Null)
                        return defaultValue;
                    var value = token.ToObject<T>();
                    return value is null ? defaultValue : value;
                }
                catch
                {
                    // Old builds may have stored a different type. One incompatible value should fall back to its
                    // current default without discarding the rest of the settings file.
                    return defaultValue;
                }
            }
        }

        public void Set<T>(string name, T value)
        {
            lock (sync)
            {
                values[name] = ToToken(value);
                QueueWriteLocked();
            }
        }

        public void Reset()
        {
            lock (sync)
            {
                values = new JObject();
                QueueWriteLocked();
            }
        }

        private static JToken ToToken<T>(T value)
        {
            if (value == null)
                return JValue.CreateNull();
            if (value is string || value is long || value is int || value is short || value is sbyte || value is ulong ||
                value is uint || value is ushort || value is byte || value is Enum || value is double || value is float ||
                value is decimal || value is DateTime || value is byte[] || value is bool || value is Guid || value is Uri ||
                value is TimeSpan)
                return new JValue(value);
            return JToken.FromObject(value);
        }

        private void QueueWriteLocked()
        {
            writeRequested = true;
            if (writerRunning)
                return;
            writerRunning = true;
            writerTask = Task.Run(DrainWritesAsync);
        }

        private async Task DrainWritesAsync()
        {
            while (true)
            {
                string snapshot;
                lock (sync)
                {
                    if (!writeRequested)
                    {
                        writerRunning = false;
                        return;
                    }
                    writeRequested = false;
                    snapshot = values.ToString(Formatting.Indented);
                }

                try
                {
                    await writer(path, snapshot);
                    lock (sync) LastWriteError = null;
                }
                catch (Exception ex)
                {
                    lock (sync) LastWriteError = ex;
                    try { onWriteError?.Invoke(ex); } catch { }
                }
            }
        }

        /// <summary>Waits until every change queued before or during this call has reached a terminal write.</summary>
        public async Task FlushAsync()
        {
            while (true)
            {
                Task pending;
                lock (sync) pending = writerTask;
                await pending;
                lock (sync)
                {
                    if (!writerRunning && !writeRequested)
                        return;
                }
            }
        }
    }
}
