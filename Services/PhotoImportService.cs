using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AirPhotoGarage.Models;

namespace AirPhotoGarage.Services;

public interface IPhotoImportService
{
    /// <summary>
    /// 导入一张照片：复制到本地库目录、生成缩略图、读取 EXIF、（可选）调用识别器。
    /// 返回的 <see cref="Photo"/> 字段中 <see cref="Photo.Id"/> 仍为 0，调用方需负责写入数据库。
    /// </summary>
    Task<Photo> PrepareImportAsync(string sourceFilePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// 照片导入服务。
/// 负责：
/// 1. 复制原图到 LocalAppData\Photos\yyyy\MM\ 目录下（避免单目录文件过多）。
/// 2. 生成 480px 缩略图到 LocalAppData\Thumbnails\。
/// 3. 读取 EXIF 元数据。
/// 4. 调用 <see cref="IAircraftRecognizer"/>（默认 <see cref="NullAircraftRecognizer"/>，不阻塞）。
/// </summary>
public sealed class PhotoImportService : IPhotoImportService
{
    private readonly string _photosRoot;
    private readonly string _thumbsRoot;
    private readonly IExifService _exifService;
    private readonly IAircraftRecognizer _recognizer;

    public PhotoImportService(
        string libraryRoot,
        IExifService exifService,
        IAircraftRecognizer recognizer)
    {
        _photosRoot = Path.Combine(libraryRoot, "Photos");
        _thumbsRoot = Path.Combine(libraryRoot, "Thumbnails");
        Directory.CreateDirectory(_photosRoot);
        Directory.CreateDirectory(_thumbsRoot);
        _exifService = exifService;
        _recognizer = recognizer;
    }

    public async Task<Photo> PrepareImportAsync(string sourceFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("Source photo not found.", sourceFilePath);
        }

        var info = new FileInfo(sourceFilePath);
        var ext = info.Extension.ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) ext = ".jpg";

        // 目标路径：按拍摄年月分目录
        var exif = await _exifService.ReadAsync(sourceFilePath);
        var anchor = exif.ShotAt ?? info.LastWriteTime;
        var relDir = Path.Combine(anchor.ToLocalTime().ToString("yyyy"), anchor.ToLocalTime().ToString("MM"));
        Directory.CreateDirectory(Path.Combine(_photosRoot, relDir));
        Directory.CreateDirectory(Path.Combine(_thumbsRoot, relDir));

        var baseName = $"{anchor.ToLocalTime():yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{ext}";
        var destPath = Path.Combine(_photosRoot, relDir, baseName);
        var thumbPath = Path.Combine(_thumbsRoot, relDir, baseName);

        await CopyAsync(sourceFilePath, destPath, cancellationToken);

        await ThumbnailGenerator.GenerateAsync(destPath, thumbPath, maxSide: 480, cancellationToken);

        var photo = new Photo
        {
            FilePath = destPath,
            ThumbnailPath = thumbPath,
            FileSize = new FileInfo(destPath).Length,
            ShotAt = exif.ShotAt,
            CameraMake = exif.CameraMake,
            CameraModel = exif.CameraModel,
            LensModel = exif.LensModel,
            FocalLength = exif.FocalLength,
            Aperture = FormatAperture(exif.ApertureFNumber),
            ShutterSpeed = FormatShutter(exif.ExposureTimeSeconds),
            Iso = exif.Iso,
            Latitude = exif.Latitude,
            Longitude = exif.Longitude,
            ImportedAt = DateTimeOffset.Now,
        };

        // 异步识别（不阻塞 UI；当前默认实现立即返回 null）
        if (_recognizer is not NullAircraftRecognizer)
        {
            try
            {
                var result = await _recognizer.RecognizeAsync(destPath, cancellationToken);
                if (result is not null)
                {
                    photo.RecognitionStatus = 1;
                    photo.RecognizedAircraftModel = result.AircraftModel;
                    photo.RecognitionConfidence = result.Confidence;
                }
            }
            catch
            {
                // 识别失败不影响主流程
            }
        }

        return photo;
    }

    private static async Task CopyAsync(string src, string dst, CancellationToken ct)
    {
        await using var s = File.OpenRead(src);
        await using var d = File.Create(dst);
        await s.CopyToAsync(d, ct);
    }

    private static string? FormatAperture(double? f)
    {
        if (!f.HasValue) return null;
        return $"f/{f.Value:0.#}";
    }

    private static string? FormatShutter(double? seconds)
    {
        if (!seconds.HasValue) return null;
        if (seconds.Value >= 1) return $"{seconds.Value:0.#}s";
        var denom = Math.Round(1.0 / seconds.Value);
        return $"1/{denom:0}s";
    }
}
