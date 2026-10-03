using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Typedown.Core.Enums;
using Typedown.Core.Models;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;

namespace Typedown.Core.Services
{
    public class ImageUpload
    {
        public ObservableCollection<ImageUploadConfig> ImageUploadConfigs { get; } = new();

        private IServiceProvider serviceProvider;

        public ImageUpload(IServiceProvider serviceProvider, SettingsViewModel settings)
        {
            this.serviceProvider = serviceProvider;
            settings.ResetSettingsCommand.OnExecute.Subscribe(async _ => await ResetDefaultConfigs());
            Initialize(settings);
        }

        private async void Initialize(SettingsViewModel settings)
        {
            if (!settings.ImageUploadDatabaseInitialized)
            {
                settings.ImageUploadDatabaseInitialized = true;
                await ResetDefaultConfigs();
            }
            else
            {
                await UpdateImageUploadConfigs();
            }
        }

        private async Task ResetDefaultConfigs()
        {
            using var ctx = await AppDbContext.Create();
            ctx.ImageUploadConfigs.RemoveRange(ctx.ImageUploadConfigs);
            await ctx.SaveChangesAsync();
            await UpdateImageUploadConfigs();
        }

        public async Task<ImageUploadConfig> AddImageUploadConfig(string name = null, ImageUploadMethod method = 0)
        {
            using var ctx = await AppDbContext.Create();
            var model = ctx.ImageUploadConfigs;
            var res = new ImageUploadConfig() { Name = name ?? string.Empty, Method = method };
            await model.AddAsync(res);
            await ctx.SaveChangesAsync();
            await UpdateImageUploadConfigs();
            return res;
        }

        public async Task RemoveImageUploadConfig(int id)
        {
            using var ctx = await AppDbContext.Create();
            var model = ctx.ImageUploadConfigs;
            model.RemoveRange(model.Where(x => x.Id == id));
            await ctx.SaveChangesAsync();
            await UpdateImageUploadConfigs();
        }

        public async Task<bool> SaveImageUploadConfig(ImageUploadConfig config)
        {
            try
            {
                using var ctx = await AppDbContext.Create();
                var model = ctx.ImageUploadConfigs;
                model.Update(config);
                await ctx.SaveChangesAsync();
                await UpdateImageUploadConfigs();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task<ImageUploadConfig> GetImageUploadConfig(int id)
        {
            using var ctx = await AppDbContext.Create();
            var model = ctx.ImageUploadConfigs;
            return await model.Where(x => x.Id == id).FirstOrDefaultAsync();
        }

        public async Task UpdateImageUploadConfigs()
        {
            using var ctx = await AppDbContext.Create();
            var newItems = await ctx.ImageUploadConfigs.ToListAsync();
            ImageUploadConfigs.UpdateCollection(newItems, (a, b) => a.Id == b.Id);
        }

        /// <summary>The configuration uploads use (Settings > Image > Upload with), or null when none is chosen and enabled.</summary>
        public ImageUploadConfig DefaultConfig
        {
            get
            {
                var id = serviceProvider.GetService<SettingsViewModel>().DefaultImageUploadConfigId;
                return id.HasValue ? ImageUploadConfigs.FirstOrDefault(x => x.IsEnable && x.Id == id.Value) : null;
            }
        }

        public async Task<string> Upload(ImageAction.InsertImageSource source, string filePath)
        {
            if (DefaultConfig is not ImageUploadConfig config)
                throw new InvalidOperationException(Locale.GetDialogString("UploadImages.NoConfig"));
            return await config.LoadUploadConfig().Upload(serviceProvider, filePath);
        }

        public async Task<string> Upload(ImageUploadConfig config, string filePath)
        {
            return await config.LoadUploadConfig().Upload(serviceProvider, filePath);
        }
    }
}
