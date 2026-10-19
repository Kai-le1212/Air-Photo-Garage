using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace AirPhotoGarage.ViewModels;

/// <summary>
/// 分组头图的解码入口。
///
/// <para>
/// 统一按 <see cref="DecodePixelHeight"/> 高度解码：行内缩略图只有 96×64、窗格封面 240 宽，
/// 按原始尺寸解码一张 6000px 的大图纯属浪费内存。
/// 另外解码后 <c>PixelWidth/PixelHeight</c> 就是<b>原始宽高比</b>，
/// 窗格封面靠它算高度（上下不留空、也不裁切）。
/// </para>
/// </summary>
public static class CoverImageLoader
{
    public const int DecodePixelHeight = 480;

    /// <summary>读取失败（文件不在、格式不支持）时返回 null，由调用方决定怎么降级。</summary>
    public static async Task<BitmapImage?> LoadAsync(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

        try
        {
            var bitmap = new BitmapImage { DecodePixelHeight = DecodePixelHeight };
            using var stream = File.OpenRead(path);
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>宽高比（宽 ÷ 高）；拿不到时返回 0。</summary>
    public static double AspectOf(BitmapImage? bitmap) =>
        bitmap is not null && bitmap.PixelHeight > 0
            ? (double)bitmap.PixelWidth / bitmap.PixelHeight
            : 0;
}
