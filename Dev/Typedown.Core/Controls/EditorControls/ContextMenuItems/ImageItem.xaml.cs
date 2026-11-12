using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Typedown.Core.Interfaces;
using Typedown.Core.Models;
using Typedown.Core.Models.RuntimeModels;
using Typedown.Core.Services;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Windows.Storage.Pickers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.Controls.EditorControls.ContextMenuItems
{
    public sealed partial class ImageItem : MenuItemCollection, INotifyPropertyChanged
    {
        public AppViewModel ViewModel { get; private set; }

        public ImageUpload ImageUpload => ViewModel.ServiceProvider.GetService<ImageUpload>();

        public IFileOperation FileOperation => ViewModel.ServiceProvider.GetService<IFileOperation>();

        public ImageAction ImageAction => ViewModel.ServiceProvider.GetService<ImageAction>();

        public JToken SelectedImage { get; private set; }

        public string ImageSrc => SelectedImage?["token"]?["src"]?.ToString() ?? "";

        public string ImageAlt => SelectedImage?["token"]?["alt"]?.ToString() ?? "";

        public string ImageTitle => SelectedImage?["token"]?["title"]?.ToString() ?? "";

        public ImageItem()
        {
            this.InitializeComponent();
        }

        private void OnImageMenuLoaded(object sender, RoutedEventArgs e)
        {
            ViewModel = (sender as FrameworkElement).GetService<AppViewModel>();
            SelectedImage = ViewModel.EditorViewModel.Selection["selectedImage"];
            UpdateMenuItemState();
            UpdateImageUploadConfigItem();
        }

        private void UpdateMenuItemState()
        {
            var isLocalImage = UriHelper.TryGetLocalPath(ImageSrc, out _);
            var embedded = ImageData.IsDataUri(ImageSrc);
            // Reading mode changes nothing: not the document's reference (copy or move and point there, upload), not the
            // files on disk (move, delete) - only a copy kept elsewhere and where the picture is (the report's M01).
            var editable = !ReadOnly;
            OpenImageLocationItem.IsEnabled = isLocalImage;
            CopyImageItem.IsEnabled = editable;
            MoveImageItem.IsEnabled = isLocalImage && editable;
            UploadSubMenu.IsEnabled = editable;
            DeleteImageItem.IsEnabled = isLocalImage && editable;
            ExtractImageItem.Visibility = embedded ? Visibility.Visible : Visibility.Collapsed;
            EmbedImageItem.Visibility = embedded ? Visibility.Collapsed : Visibility.Visible;
            ExtractImageItem.IsEnabled = EmbedImageItem.IsEnabled = ReplaceImageItem.IsEnabled = editable;
        }

        // Asked again when an item runs, not only when the menu opened: the mode can change while it is open, and an
        // item must not act on a document shown read-only.
        private bool ReadOnly => ViewModel?.SettingsViewModel?.ReadOnly ?? true;

        private void UpdateImageUploadConfigItem()
        {
            // The window's upload service reads its configurations when first asked: the first menu after a start
            // would list none, so it fills in once they are read.
            if (!ImageUpload.Ready.IsCompleted)
                _ = ImageUpload.Ready.ContinueWith(_ => UpdateImageUploadConfigItem(), System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
            var configs = ImageUpload.ImageUploadConfigs.Where(x => x.IsEnable).ToList();
            foreach (var old in UploadSubMenu.Items.Where(x => x.Tag is ImageUploadConfig).ToList())
                UploadSubMenu.Items.Remove(old);
            // Only a file can be uploaded: for any other picture the services are listed, not offered, under the reason.
            var isLocalImage = UriHelper.TryGetLocalPath(ImageSrc, out _);
            var at = UploadSubMenu.Items.IndexOf(NoUploadConfigItem) + 1;
            foreach (var config in configs.Reverse<ImageUploadConfig>())
            {
                var item = new MenuFlyoutItem() { Text = config.Name, Tag = config, IsEnabled = isLocalImage };
                item.Click += (s, e) => OnUploadImageClick(config);
                UploadSubMenu.Items.Insert(at, item);
            }
            UploadLocalOnlyItem.Visibility = isLocalImage ? Visibility.Collapsed : Visibility.Visible;
            NoUploadConfigItem.Visibility = configs.Any() ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnOpenImageLocationClick(object sender, RoutedEventArgs e)
        {
            try
            {
                UriHelper.TryGetLocalPath(ImageSrc, out var path);
                var fullPath = Path.GetFullPath(Path.Combine(ViewModel.FileViewModel.ImageBasePath, path));
                Common.OpenFileLocation(fullPath);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private async void OnCopyImageToClick(object sender, RoutedEventArgs e)
        {
            if (ReadOnly) return;
            try
            {
                byte[] bytes = await GetImageBytes();
                if (bytes == null)
                    return;
                var file = await PickImageSavePath(bytes);
                if (file == null)
                    return;
                await File.WriteAllBytesAsync(file, bytes);
                file = ImageAction.ConvertImagePath(file);
                ReplaceImage(new(file, ImageAlt, ImageTitle));
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private async void OnMoveImageToClick(object sender, RoutedEventArgs e)
        {
            if (ReadOnly) return;
            try
            {
                byte[] bytes = await GetImageBytes();
                if (bytes == null)
                    return;
                var file = await PickImageSavePath(bytes);
                if (file == null)
                    return;
                await File.WriteAllBytesAsync(file, bytes);
                file = ImageAction.ConvertImagePath(file);
                ReplaceImage(new(file, ImageAlt, ImageTitle));
                if (UriHelper.TryGetLocalPath(ImageSrc, out var path))
                {
                    path = ViewModel.GetImageAbsolutePath(path);
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        // The picture kept in the document saved as a file in the image folder (Settings > Image), and pointed at there.
        private async void OnExtractImageClick(object sender, RoutedEventArgs e)
        {
            if (ReadOnly) return;
            try
            {
                if (!ImageData.TryParseDataUri(ImageSrc, out var bytes, out var extension)) return;
                var name = string.IsNullOrWhiteSpace(ImageAlt) ? null : $"{FileNameOf(ImageAlt)}.{extension}";
                var saved = await ImageAction.SaveImage(ImageAction.InsertImageSource.Local, bytes, name ?? $"{Guid.NewGuid()}.{extension}");
                ReplaceImage(new(DocumentAddress(ViewModel.GetImageAbsolutePath(saved)), ImageAlt, ImageTitle));
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        // The picture (a file, or on the web) written into the document itself as a data: address.
        private async void OnEmbedImageClick(object sender, RoutedEventArgs e)
        {
            if (ReadOnly) return;
            try
            {
                var bytes = await GetImageBytes();
                if (bytes == null) return;
                ReplaceImage(new(ImageData.ToDataUri(bytes), ImageAlt, ImageTitle));
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        // Another picture in its place, kept the way this one is: in the document if it was, else as Settings > Image says.
        private async void OnReplaceImageClick(object sender, RoutedEventArgs e)
        {
            if (ReadOnly) return;
            try
            {
                var picker = new FileOpenPicker();
                foreach (var type in new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".bmp" }) picker.FileTypeFilter.Add(type);
                picker.SetOwnerWindow(ViewModel.MainWindow);
                var file = await FilePickersExtensions.ShowAsync(() => picker.PickSingleFileAsync());
                if (file == null || ReadOnly) return;
                var src = ImageData.IsDataUri(ImageSrc)
                    ? ImageData.ToDataUri(await File.ReadAllBytesAsync(file.Path))
                    : await ImageAction.DoLocalFileAction(new Uri(file.Path).AbsoluteUri);
                ReplaceImage(new(src, ImageAlt, ImageTitle));
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        // A file saved for the document: written relative to it as Settings > Image prefers; an untitled one has no folder.
        private string DocumentAddress(string absolutePath) =>
            string.IsNullOrEmpty(ViewModel.FileViewModel.FilePath) ? new Uri(absolutePath).AbsoluteUri : ImageAction.ConvertImagePath(absolutePath);

        private static string FileNameOf(string text)
        {
            var name = string.Concat(text.Trim().Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            return name.Length > 60 ? name.Substring(0, 60) : name;
        }

        private async void OnUploadImageClick(ImageUploadConfig config)
        {
            if (ReadOnly) return;
            try
            {
                if (UriHelper.TryGetLocalPath(ImageSrc, out var path))
                {
                    path = ViewModel.GetImageAbsolutePath(path);
                    var uri = await ImageUpload.Upload(config, path);
                    ReplaceImage(new(uri, ImageAlt, ImageTitle));
                }
            }
            catch (Exception ex)
            {
                await AppContentDialog.Create(Locale.GetDialogString("UploadFailedTitle"), ex.Message, "Ok").ShowAsync(ViewModel.XamlRoot);
            }
        }

        private void OnImageUploadSettingsClick(object sender, RoutedEventArgs e)
        {
            ViewModel.NavigateCommand.Execute("Settings/Image");
        }

        private async void OnSaveImageClick(object sender, RoutedEventArgs e)
        {
            try
            {
                byte[] bytes = await GetImageBytes();
                if (bytes == null)
                    return;
                var file = await PickImageSavePath(bytes);
                if (file == null)
                    return;
                await File.WriteAllBytesAsync(file, bytes);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private void OnDeleteImageFileClick(object sender, RoutedEventArgs e)
        {
            if (ReadOnly) return;
            try
            {
                if (UriHelper.TryGetLocalPath(ImageSrc, out var path))
                {
                    path = ViewModel.GetImageAbsolutePath(path);
                    if (FileOperation.Delete(new StringCollection() { path }))
                    {
                        ViewModel.EditorViewModel.DeleteSelectionCommand.Execute(null);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        // A picture operation that failed (the file gone or locked, a folder not writable) says why: these were silent.
        private async void ShowError(Exception ex)
        {
            Log.Debug($"picture menu: {ex}");
            try { await AppContentDialog.Create(Locale.GetString("Error"), ex.Message, Locale.GetString("Ok")).ShowAsync(ViewModel?.XamlRoot); }
            catch { }
        }

        private void ReplaceImage(HtmlImgTag htmlImgTag)
        {
            ViewModel.MarkdownEditor.PostMessage("ReplaceImage", new
            {
                htmlImgTag.Src,
                htmlImgTag.Alt,
                htmlImgTag.Title,
                IsReplaceSelected = true
            });
        }

        private async Task<string> PickImageSavePath(byte[] bytes)
        {
            var filePicker = new FileSavePicker();
            var type = ImageAction.GetImageType(bytes, "png");
            filePicker.FileTypeChoices.Add(type, new List<string>() { $".{type}" });
            filePicker.SuggestedFileName = ImageAlt;
            filePicker.SetOwnerWindow(ViewModel.MainWindow);
            var file = await FilePickersExtensions.ShowAsync(() => filePicker.PickSaveFileAsync());
            return file?.Path;
        }

        private async Task<byte[]> GetImageBytes()
        {
            if (ImageData.TryParseDataUri(ImageSrc, out var embedded, out _))
                return embedded;
            if (UriHelper.IsWebUrl(ImageSrc))
            {
                return await ImageAction.GetWebImage(new(ImageSrc));
            }
            else if (UriHelper.TryGetLocalPath(ImageSrc, out var path))
            {
                path = ViewModel.GetImageAbsolutePath(path);
                return await File.ReadAllBytesAsync(path);
            }
            return null;
        }
    }
}
