using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Typedown.Core.Enums;
using Typedown.Core.Interfaces;
using Typedown.Core.Models;
using Typedown.Core.Services;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Typedown.Controls;

namespace Typedown.Services
{
    public class FileExport : IFileExport, IDisposable
    {
        public ObservableCollection<ExportConfig> ExportConfigs { get; } = new();

        public AppViewModel ViewModel { get; }

        public IFileConverter FileConverter { get; }

        private readonly CompositeDisposable disposables = new();

        public FileExport(AppViewModel viewModel, SettingsViewModel settings, IFileConverter fileConverter)
        {
            ViewModel = viewModel;
            FileConverter = fileConverter;
            disposables.Add(settings.ResetSettingsCommand.OnExecute.Subscribe(async _ =>
            {
                try { await ResetDefaultConfigs(); }
                catch (Exception ex) { Log.WriteLocal("FileExportReset", ex.ToString()); }
            }));
            Initialize(settings);
        }

        // The default configurations, written once for all windows; marked written only once they are (marked first, a
        // failure left the database with none, never tried again).
        private static Task seeding;

        private async void Initialize(SettingsViewModel settings)
        {
            try
            {
                if (!settings.FileExportDatabaseInitialized)
                {
                    seeding ??= SeedAsync(settings);
                    await seeding;
                }
                else
                {
                    await UpdateExportConfigs();
                }
            }
            catch (Exception ex)
            {
                Log.WriteLocal("FileExportInitialize", ex.ToString());
            }
        }

        private async Task SeedAsync(SettingsViewModel settings)
        {
            await ResetDefaultConfigs();
            settings.FileExportDatabaseInitialized = true;
        }

        private async Task ResetDefaultConfigs()
        {
            await ConfigStore.ClearExportConfigsAsync();
            await AddExportConfig("PDF", ExportType.PDF);
            await AddExportConfig("HTML", ExportType.HTML);
            await UpdateExportConfigs();
        }

        public async Task<ExportConfig> AddExportConfig(string name = null, ExportType type = 0)
        {
            var res = await ConfigStore.AddExportConfigAsync(name, type);
            await UpdateExportConfigs();
            return res;
        }

        public async Task RemoveExportConfig(int id)
        {
            await ConfigStore.RemoveExportConfigAsync(id);
            await UpdateExportConfigs();
        }

        public async Task<bool> SaveExportConfig(ExportConfig config)
        {
            try
            {
                await ConfigStore.SaveExportConfigAsync(config);
                await UpdateExportConfigs();
                return true;
            }
            catch (Exception ex)
            {
                Log.WriteLocal("ExportConfigSave", ex.ToString());
                return false;
            }
        }

        public async Task<ExportConfig> GetExportConfig(int id)
        {
            return await ConfigStore.ExportConfigAsync(id);
        }

        public async Task UpdateExportConfigs()
        {
            var newItems = await ConfigStore.ExportConfigsAsync();
            ExportConfigs.UpdateCollection(newItems, (a, b) => a.Id == b.Id);
        }

        public Task Print(string basePath, string html, string documentName = null)
        {
            var preview = new PrintPreviewControl(html, documentName);
            // The preview leaves with the print dialog; its close button stays for a dialog that never came.
            preview.PrintDismissed += () => { if (ViewModel.UIViewModel.PrintPreviewContent == preview) ViewModel.UIViewModel.ClosePrintPreview(); };
            preview.PrintFailed += async message =>
            {
                if (ViewModel.UIViewModel.PrintPreviewContent == preview) ViewModel.UIViewModel.ClosePrintPreview();
                try { await Core.Controls.AppContentDialog.Create(Core.Utilities.Locale.GetString("Error"), message, Core.Utilities.Locale.GetString("Ok")).ShowAsync(ViewModel.XamlRoot); }
                catch (Exception ex) { Core.Utilities.Log.Debug($"print: could not show the failure: {ex.Message}"); }
            };
            ViewModel.UIViewModel.ShowPrintPreview(preview, documentName);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
