using System;

namespace AirPhotoGarage.Models;

/// <summary>
/// 从图片中读取出的 EXIF 元数据。后续可被导入流程填充到 <see cref="Photo"/>。
/// </summary>
public sealed class ExifMetadata
{
    public DateTimeOffset? ShotAt { get; set; }
    public string? CameraMake { get; set; }
    public string? CameraModel { get; set; }
    public string? LensModel { get; set; }
    public double? FocalLength { get; set; }
    public double? ApertureFNumber { get; set; }
    public double? ExposureTimeSeconds { get; set; }
    public int? Iso { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
}
