using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AirPhotoGarage.Models;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Icc;
using MetadataExtractor.Formats.Xmp;
using XmpCore;

namespace AirPhotoGarage.Services;

public interface IExifService
{
    Task<ExifMetadata> ReadAsync(string filePath);
}

/// <summary>
/// 基于 <c>MetadataExtractor</c> 实现的元数据读取。
/// 该库无需任何系统原生依赖，跨平台支持良好，且能够正确解析主流厂商 RAW/JPG/HEIC。
///
/// <para>
/// <b>两条数据源，EXIF 优先、XMP 兜底</b>：不少照片（尤其是 Lightroom 导出的 PNG）
/// <b>没有 EXIF IFD</b>，全部参数只存在于 PNG 的 <c>iTXt</c> 块里的 XMP。
/// 早期实现只读 <see cref="ExifSubIfdDirectory"/> / <see cref="ExifIfd0Directory"/>，
/// 于是这类照片在界面上表现为「EXIF 一片空白」，看起来像「读不到」。
/// 现在 EXIF 读到的字段优先，缺的再从 XMP 补。
/// </para>
/// </summary>
public sealed class ExifService : IExifService
{
    public Task<ExifMetadata> ReadAsync(string filePath) => Task.Run(() => Read(filePath));

    private static ExifMetadata Read(string filePath)
    {
        var meta = new ExifMetadata();

        var directories = ImageMetadataReader.ReadMetadata(filePath);

        // ---------- 1) 标准 EXIF ----------

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

        // 图像尺寸（EXIF 的 PixelXDimension/PixelYDimension，只有 ifd0 存在时才有）
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

        // PNG 没有 EXIF IFD，尺寸只能从 IHDR 块拿（tag 1/2 分别是宽/高）。
        if (meta.ImageWidth <= 0 || meta.ImageHeight <= 0)
        {
            var ihdr = directories.FirstOrDefault(d => d.Name == "PNG-IHDR");
            if (ihdr is not null)
            {
                if (meta.ImageWidth <= 0 && ihdr.TryGetInt32(1, out var pw)) meta.ImageWidth = pw;
                if (meta.ImageHeight <= 0 && ihdr.TryGetInt32(2, out var ph)) meta.ImageHeight = ph;
            }
        }

        // ---------- 2) XMP 兜底 ----------
        var xmp = ReadXmp(directories);
        if (xmp is not null)
        {
            FillFromXmp(meta, xmp);
        }

        // ---------- 3) 原始元数据快照（存进 Photo.ExifJson） ----------
        meta.RawDump = BuildRawDump(directories, xmp);

        return meta;
    }

    /// <summary>把 XMP 的「路径 → 值」摊平成字典；没有 XMP 块时返回 null。</summary>
    private static Dictionary<string, string>? ReadXmp(IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        var xmpDir = directories.OfType<XmpDirectory>().FirstOrDefault();
        if (xmpDir is null) return null;

        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        // XMP 块存在但解析失败时 XmpMeta 为 null —— 当作没有 XMP，不要让它把整次读取搞崩
        var xmpMeta = xmpDir.XmpMeta;
        if (xmpMeta is null) return null;

        foreach (var prop in xmpMeta.Properties)
        {
            // 数组/结构体的「容器」节点 Path 为空，值在子节点里（如 exif:ISOSpeedRatings[1]）
            if (string.IsNullOrEmpty(prop.Path)) continue;
            map[prop.Path] = prop.Value ?? string.Empty;
        }
        return map;
    }

    /// <summary>
    /// 用 XMP 填补 EXIF 没给出的字段（<b>只填空缺，不覆盖</b> EXIF 已读到的值）。
    ///
    /// <para>
    /// 注意时区：XMP 的 <c>exif:DateTimeOriginal</c> 是带偏移的 ISO 8601
    /// （如 <c>2026-06-11T17:43:51.907+08:00</c>），而本项目的 <see cref="Photo.ShotAt"/>
    /// 约定存「墙上时间 + TimeSpan.Zero」。所以取 <c>.DateTime</c>（字面量）再标 Zero，
    /// <b>不能</b>保留原偏移，否则整库时间会按本机时区平移。
    /// </para>
    /// </summary>
    private static void FillFromXmp(ExifMetadata meta, IReadOnlyDictionary<string, string> xmp)
    {
        string? Get(params string[] keys)
        {
            foreach (var key in keys)
            {
                if (xmp.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)) return v.Trim();
            }
            return null;
        }

        meta.CameraMake ??= Get("tiff:Make");
        meta.CameraModel ??= Get("tiff:Model");
        meta.LensModel ??= Get("aux:Lens", "exifEX:LensModel", "LensModel");

        meta.FocalLength ??= ParseRational(Get("exif:FocalLength"));
        meta.ApertureFNumber ??= ParseRational(Get("exif:FNumber"));
        meta.ExposureTimeSeconds ??= ParseRational(Get("exif:ExposureTime"));

        // ISOSpeedRatings 是数组，值实际落在 [1] 上
        meta.Iso ??= ParseInt(Get("exif:ISOSpeedRatings[1]", "exif:ISO", "exif:PhotographicSensitivity"));

        if (meta.ShotAt is null
            && Get("exif:DateTimeOriginal", "photoshop:DateCreated", "xmp:CreateDate") is { } rawShot
            && DateTimeOffset.TryParse(rawShot, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
        {
            meta.ShotAt = new DateTimeOffset(dto.DateTime, TimeSpan.Zero);
        }
    }

    /// <summary>
    /// 构建存进 <c>ExifJson</c> 的扁平快照：键形如「Exif SubIFD · Lens Model」。
    ///
    /// <para>
    /// 跳过两类：<see cref="XmpDirectory"/>（在下面单独展开，否则只是一个「XMP Value Count」），
    /// 以及 <see cref="IccDirectory"/>（色彩配置的内部字段，标签名是一串数字，纯噪声）。
    /// XMP 里的 <c>crs:</c> 命名空间是 Lightroom 的调色参数，与照片参数无关，一并跳过。
    /// </para>
    /// </summary>
    private static string? BuildRawDump(
        IReadOnlyList<MetadataExtractor.Directory> directories,
        IReadOnlyDictionary<string, string>? xmp)
    {
        var flat = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var dir in directories)
        {
            if (dir is XmpDirectory || dir is IccDirectory) continue;

            foreach (var tag in dir.Tags)
            {
                var desc = tag.Description;
                if (string.IsNullOrWhiteSpace(desc)) continue;
                flat[$"{dir.Name} · {tag.Name}"] = desc.Trim();
            }
        }

        if (xmp is not null)
        {
            foreach (var (path, value) in xmp)
            {
                if (path.StartsWith("crs:", StringComparison.Ordinal)) continue;
                if (string.IsNullOrWhiteSpace(value)) continue;
                flat[$"XMP · {path}"] = value.Trim();
            }
        }

        return flat.Count == 0 ? null : JsonSerializer.Serialize(flat);
    }

    private static string? Trim(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        return s.Length == 0 ? null : s;
    }

    /// <summary>
    /// 解析 XMP 里的数值：既可能是 rational（<c>2000/10</c>、<c>63/10</c>、<c>1/250</c>），
    /// 也可能是普通小数。无法解析时返回 null。
    /// </summary>
    private static double? ParseRational(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var s = raw.Trim();
        var slash = s.IndexOf('/');
        if (slash > 0
            && double.TryParse(s[..slash], NumberStyles.Float, CultureInfo.InvariantCulture, out var num)
            && double.TryParse(s[(slash + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var den)
            && den != 0)
        {
            return num / den;
        }

        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static int? ParseInt(string? raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

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
