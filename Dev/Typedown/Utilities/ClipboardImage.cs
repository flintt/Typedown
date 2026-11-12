using System;
using System.IO;
using System.Threading.Tasks;
using Typedown.Core.Interfaces;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Typedown.Utilities
{
    public class ClipboardImage : IClipboardImage
    {
        private readonly RandomAccessStreamReference bitmapStreamRef;

        public ClipboardImage(RandomAccessStreamReference bitmapStreamRef)
        {
            this.bitmapStreamRef = bitmapStreamRef;
        }

        public byte[] GetBytes()
        {
            return GetBytesAsync().GetAwaiter().GetResult();
        }

        private async Task<byte[]> GetBytesAsync()
        {
            using var stream = await bitmapStreamRef.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var pixelData = await decoder.GetPixelDataAsync();
            var bytes = pixelData.DetachPixelData();

            using var memoryStream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, memoryStream);
            encoder.SetPixelData(
                decoder.BitmapPixelFormat,
                decoder.BitmapAlphaMode,
                decoder.PixelWidth,
                decoder.PixelHeight,
                decoder.DpiX,
                decoder.DpiY,
                bytes);
            await encoder.FlushAsync();

            var result = new byte[memoryStream.Size];
            await memoryStream.AsStream().ReadAsync(result, 0, result.Length);
            return result;
        }

        public byte[] GetJpegBytes(int quality)
        {
            return GetJpegBytesAsync(quality).GetAwaiter().GetResult();
        }

        private async Task<byte[]> GetJpegBytesAsync(int quality)
        {
            using var stream = await bitmapStreamRef.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var pixelData = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
                new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            var bytes = pixelData.DetachPixelData();
            // A JPEG has no transparency: what was see-through is put on white (it came out black).
            for (var i = 0; i + 3 < bytes.Length; i += 4)
            {
                var alpha = bytes[i + 3];
                if (alpha == 255) continue;
                for (var k = 0; k < 3; k++) bytes[i + k] = (byte)((bytes[i + k] * alpha + 255 * (255 - alpha)) / 255);
                bytes[i + 3] = 255;
            }

            using var memoryStream = new InMemoryRandomAccessStream();
            var options = new BitmapPropertySet { { "ImageQuality", new BitmapTypedValue(Math.Clamp(quality, 1, 100) / 100.0f, global::Windows.Foundation.PropertyType.Single) } };
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, memoryStream, options);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, decoder.PixelWidth, decoder.PixelHeight, decoder.DpiX, decoder.DpiY, bytes);
            await encoder.FlushAsync();

            var result = new byte[memoryStream.Size];
            await memoryStream.AsStream().ReadAsync(result, 0, result.Length);
            return result;
        }

        public void SaveAsPng(string path)
        {
            SaveAsPngAsync(path).GetAwaiter().GetResult();
        }

        private async Task SaveAsPngAsync(string path)
        {
            var bytes = await GetBytesAsync();
            await File.WriteAllBytesAsync(path, bytes);
        }
    }
}