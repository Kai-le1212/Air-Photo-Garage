using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace AirPhotoGarage.Services;

/// <summary>
/// 基于 Windows.Graphics.Imaging 的缩略图生成器。
/// 不依赖 WIC 原生互操作，纯 WinRT API，支持 JPEG/PNG/HEIC 等主流格式。
/// </summary>
internal static class ThumbnailGenerator
{
    public static async Task GenerateAsync(
        string sourcePath,
        string destinationPath,
        int maxSide,
        CancellationToken cancellationToken = default)
    {
        using var inputStream = File.OpenRead(sourcePath);
        var randomAccess = inputStream.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(randomAccess);

        var (origW, origH) = (decoder.PixelWidth, decoder.PixelHeight);
        if (origW == 0 || origH == 0) return;

        double scale = Math.Min(1.0, (double)maxSide / Math.Max(origW, origH));
        if (scale >= 1.0)
        {
            // 原图已经够小，直接复制字节，避免再次编码损失
            inputStream.Position = 0;
            await using var fs = File.Create(destinationPath);
            await inputStream.CopyToAsync(fs, cancellationToken);
            return;
        }

        var targetW = (uint)Math.Round(origW * scale);
        var targetH = (uint)Math.Round(origH * scale);

        var transform = new BitmapTransform
        {
            ScaledWidth = targetW,
            ScaledHeight = targetH,
            InterpolationMode = BitmapInterpolationMode.Fant,
        };

        var pixelData = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);

        // 输出 JPEG
        using var outputStream = File.Create(destinationPath);
        var outRandomAccess = outputStream.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outRandomAccess);

        // IBuffer.CreateCopyFromBuffer 需要 IBuffer，所以用 AsBuffer() 将 byte[] 包装为 IBuffer
        var buffer = pixelData.DetachPixelData().AsBuffer();
        var softwareBitmap = SoftwareBitmap.CreateCopyFromBuffer(
            buffer,
            BitmapPixelFormat.Bgra8,
            (int)targetW,
            (int)targetH);
        encoder.SetSoftwareBitmap(softwareBitmap);
        await encoder.FlushAsync();
    }
}
