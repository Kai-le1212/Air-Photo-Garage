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

        // 目标路径：按拍摄年月分目录（沿用原有口径，暂不改动）
        var exif = await _exifService.ReadAsync(sourceFilePath);
        var anchor = exif.ShotAt ?? info.LastWriteTime;
        var relDir = Path.Combine(anchor.ToLocalTime().ToString("yyyy"), anchor.ToLocalTime().ToString("MM"));
        Directory.CreateDirectory(Path.Combine(_photosRoot, relDir));
        Directory.CreateDirectory(Path.Combine(_thumbsRoot, relDir));

        // 保留原始文件名；重名才加 (2)、(3)…；
        // 同名且内容完全相同 = 已经在库里，直接跳过，不再产生重复副本。
        var originalName = Path.GetFileName(sourceFilePath);
        var destPath = Path.Combine(_photosRoot, relDir, originalName);
        if (File.Exists(destPath))
        {
            if (await IsSameContentAsync(sourceFilePath, destPath, cancellationToken))
            {
                throw new DuplicatePhotoException(sourceFilePath);
            }
            destPath = MakeUniquePath(destPath);
        }

        var thumbPath = Path.Combine(_thumbsRoot, relDir, Path.GetFileName(destPath));

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
            // 全部元数据快照：详情页「原始 EXIF 数据」读的就是这一列。
            // 此前这里漏了赋值，导致该区块对任何照片都是空的。
            ExifJson = exif.RawDump,
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

    /// <summary>光圈显示文案（<c>f/6.3</c>）。回填历史照片时也复用这一份，避免两处格式不一致。</summary>
    /// <summary>同名重名时依次尝试「name (2).ext」「name (3).ext」…</summary>
    private static string MakeUniquePath(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }

        return Path.Combine(dir, $"{name}_{Guid.NewGuid():N}{ext}");
    }

    /// <summary>两份文件是否字节完全一致（先比长度，再比 SHA-256）。</summary>
    private static async Task<bool> IsSameContentAsync(string a, string b, CancellationToken ct)
    {
        if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
        return await HashAsync(a, ct) == await HashAsync(b, ct);
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash);
    }

    internal static string? FormatAperture(double? f)
    {
        if (!f.HasValue) return null;
        return $"f/{f.Value:0.#}";
    }

    /// <summary>快门显示文案（<c>1/250s</c> / <c>2s</c>）。回填历史照片时也复用。</summary>
    internal static string? FormatShutter(double? seconds)
    {
        if (!seconds.HasValue) return null;
        if (seconds.Value >= 1) return $"{seconds.Value:0.#}s";
        var denom = Math.Round(1.0 / seconds.Value);
        return $"1/{denom:0}s";
    }
}

/// <summary>
/// 要导入的文件与库里已有的<b>同名同内容</b>，说明这张图已经导过了 —— 直接跳过，
/// 不再复制一份副本（以前的重复副本就是这么来的）。
/// </summary>
public sealed class DuplicatePhotoException : Exception
{
    public DuplicatePhotoException(string sourcePath)
        : base($"照片已在库中（同名同内容），已跳过：{Path.GetFileName(sourcePath)}")
    {
        SourcePath = sourcePath;
    }

    public string SourcePath { get; }
}
