using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Typedown.Automation;
using Typedown.Core.Enums;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;

namespace Typedown.Core.Services
{
    /// <summary>
    /// The external settings on Windows (Typedown.Automation.ISettingsHost), mapped as docs/automation-fixtures/
    /// settings-map.json says. Values are read from the shared settings store; a change goes through one window's
    /// SettingsViewModel (the same setters the settings page uses), so every window applies it the way it applies a
    /// change made in another window, and then the store is flushed so success means saved.
    /// </summary>
    public sealed class WindowsSettingsHost : ISettingsHost
    {
        private static readonly string[] BuiltIn = { "system", "light", "dark", "black" };

        private JsonSettingsStore Store => JsonSettingsStore.Shared(Path.Combine(Config.GetLocalFolderPath(), "Settings.json"));

        public long Revision => Store.Revision;

        public Task<JToken> GetAsync(string key, CancellationToken cancellationToken)
        {
            var store = Store;
            JToken value = key switch
            {
                "appearance.theme" => ThemeValue(store.Get("AppTheme", AppTheme.Default), store.Get("CustomTheme", "")),
                "editor.fontSize" => (int)Math.Round(store.Get("FontSize", 16d)),
                "editor.lineHeight" => Math.Round(store.Get("LineHeight", 1.6d), 6),
                "editor.textDirection" => store.Get("TextDirection", "auto"),
                _ => throw new AutomationException(AutomationErrorKind.setting_not_exposed, $"'{key}' is not an external setting."),
            };
            return Task.FromResult(value);
        }

        private static JObject ThemeValue(AppTheme theme, string custom) => string.IsNullOrEmpty(custom)
            ? new JObject { ["kind"] = "builtIn", ["id"] = BuiltIn[Math.Max(0, Math.Min(BuiltIn.Length - 1, (int)theme))] }
            : new JObject { ["kind"] = "custom", ["id"] = custom };

        public async Task<long> SetAsync(string key, JToken value, CancellationToken cancellationToken)
        {
            // Resolve a custom theme before anything changes: an unknown one changes nothing.
            CustomTheme customTheme = null;
            if (key == "appearance.theme" && (string)value["kind"] == "custom")
                customTheme = ThemeFiles.Find((string)value["id"])
                    ?? throw new AutomationException(AutomationErrorKind.setting_invalid, "No custom theme with that id.",
                        new Dictionary<string, object> { ["key"] = key, ["reason"] = "unknownTheme" });

            void Apply(SettingsViewModel settings)
            {
                switch (key)
                {
                    case "appearance.theme":
                        if (customTheme != null) settings.ApplyCustomTheme(customTheme);
                        else settings.ApplyBuiltInTheme((AppTheme)Array.IndexOf(BuiltIn, (string)value["id"]));
                        break;
                    case "editor.fontSize": settings.FontSize = (long)value; break;
                    case "editor.lineHeight": settings.LineHeight = (double)value; break;
                    case "editor.textDirection": settings.TextDirection = (string)value; break;
                    default: throw new AutomationException(AutomationErrorKind.setting_not_exposed, $"'{key}' is not an external setting.");
                }
            }

            var window = AutomationWindows.Registry.Snapshot().FirstOrDefault()
                ?? throw new AutomationException(AutomationErrorKind.editor_not_ready, "Typedown has no open window to apply settings in.");
            await AutomationWindows.Registry.OnWindowAsync(window.WindowId, app => { Apply(app.SettingsViewModel); return true; });

            var store = Store;
            await store.FlushAsync();
            if (store.LastWriteError != null)
                throw new AutomationException(AutomationErrorKind.persistence_failed, "The setting is applied but could not be saved.",
                    new Dictionary<string, object> { ["settingsRevision"] = store.Revision, ["applied"] = true });
            return store.Revision;
        }
    }
}
