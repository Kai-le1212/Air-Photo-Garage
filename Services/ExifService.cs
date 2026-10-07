using System;
using System.Linq;
using System.Threading.Tasks;
using AirPhotoGarage.Models;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace AirPhotoGarage.Services;

public interface IExifService
{
    Task<ExifMetadata> ReadAsync(string filePath);
}

/// <summary>
/// 基于 <c>MetadataExtractor</c> 实现的 EXIF 读取。
/// 该库无需任何系统原生依赖，跨平台支持良好，且能够正确解析主流厂商 RAW/JPG/HEIC。
/// </summary>
public sealed class ExifService : IExifService
{
    public Task<ExifMetadata> ReadAsync(string filePath)
    {
        return Task.Run(() =>
        {
            var meta = new ExifMetadata();

            var directories = ImageMetadataReader.ReadMetadata(filePath);

            // DateTimeOriginal 是相机本地时间（EXIF 规范不带时区）。
            // 我们把它视作 UTC+0 的 DateTimeOffset，UI 层直接 ToString 显示，不要 ToLocalTime 二次转换。
            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (subIfd is not null)
            {
                if (subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var shot))
                {
                    meta.ShotAt = new DateTimeOffset(shot, TimeSpan.Zero);
                }

                if (subIfd.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var iso))
                {
                    meta.Iso = iso;
                }

                if (subIfd.TryGetDouble(ExifDirectoryBase.TagFocalLength, out var focal))
                {
                    meta.FocalLength = focal;
                }

                if (subIfd.TryGetDouble(ExifDirectoryBase.TagFNumber, out var f))
                {
                    meta.ApertureFNumber = f;
                }

                if (subIfd.TryGetDouble(ExifDirectoryBase.TagExposureTime, out var exposure))
                {
                    meta.ExposureTimeSeconds = exposure;
                }
            }

            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            if (ifd0 is not null)
            {
                meta.CameraMake = Trim(ifd0.GetDescription(ExifDirectoryBase.TagMake));
                meta.CameraModel = Trim(ifd0.GetDescription(ExifDirectoryBase.TagModel));
            }

            if (subIfd is not null)
            {
                meta.LensModel = Trim(subIfd.GetDescription(ExifDirectoryBase.TagLensModel));
            }

            // GPS：通过通用的 GpsDirectory 解析（MetadataExtractor 自动识别）。
            var gpsDir = directories.FirstOrDefault(d =>
                d.Name.StartsWith("GPS", StringComparison.OrdinalIgnoreCase));
            if (gpsDir is not null)
            {
                var latStr = gpsDir.GetDescription(2); // GPSLatitude
                var lonStr = gpsDir.GetDescription(4); // GPSLongitude
                if (TryParseDms(latStr, out var latVal) && TryParseDms(lonStr, out var lonVal))
                {
                    meta.Latitude = latVal;
                    meta.Longitude = lonVal;
                }
            }

            // 图像尺寸
            var exifIfd0 = ifd0 ?? directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            if (exifIfd0 is not null)
            {
                if (exifIfd0.TryGetInt32(ExifDirectoryBase.TagImageWidth, out var w))
                {
                    meta.ImageWidth = w;
                }
                if (exifIfd0.TryGetInt32(ExifDirectoryBase.TagImageHeight, out var h))
                {
                    meta.ImageHeight = h;
                }
            }

            return meta;
        });
    }

    private static string? Trim(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        return s.Length == 0 ? null : s;
    }

    /// <summary>
    /// 解析 "37°46'29.6\"" 形式的度分秒字符串为 double。
    /// </summary>
    private static bool TryParseDms(string? raw, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var s = raw.Replace("°", " ").Replace("'", " ").Replace("\"", " ").Trim();
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1) return false;
        if (!double.TryParse(parts[0], out var deg)) return false;
        double min = 0, sec = 0;
        if (parts.Length > 1 && double.TryParse(parts[1], out var m)) min = m;
        if (parts.Length > 2 && double.TryParse(parts[2], out var ss)) sec = ss;
        value = deg + min / 60.0 + sec / 3600.0;
        if (raw.Contains("S") || raw.Contains("W")) value = -value;
        return true;
    }
}
