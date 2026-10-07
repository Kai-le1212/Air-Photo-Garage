using System;

namespace AirPhotoGarage.Models;

/// <summary>
/// 照片记录。对应 SQLite 中的一张照片元数据。
/// 字段设计原则：
/// 1. 核心检索字段（机型/注册号/机场/拍摄时间）独立成列，便于建立索引与快速筛选。
/// 2. EXIF 详细参数放在 <see cref="ExifJson"/> 中，作为 JSON 文本保存（可后续扩展为独立表）。
/// 3. <see cref="RecognitionStatus"/> + <see cref="RecognizedAircraftModel"/> 字段为未来接入 AI 模型识别预留。
/// </summary>
public sealed class Photo
{
    public long Id { get; set; }

    /// <summary>原图绝对路径（已复制到照片库本地目录后的路径）。</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>缩略图绝对路径（Library 内部维护）。</summary>
    public string? ThumbnailPath { get; set; }

    /// <summary>文件大小（字节）。</summary>
    public long FileSize { get; set; }

    /// <summary>照片拍摄时间。来自 EXIF DateTimeOriginal，缺失时回退到文件创建时间。</summary>
    public DateTimeOffset? ShotAt { get; set; }

    /// <summary>机型（如 "Boeing 737-800"、"Airbus A320neo"）。</summary>
    public string? AircraftModel { get; set; }

    /// <summary>注册号（如 "B-1234"、"N12345"）。</summary>
    public string? RegistrationNumber { get; set; }

    /// <summary>机场 ICAO / IATA 代码（兼容旧字段，新数据请用 <see cref="AirportIata"/> / <see cref="AirportIcao"/>）。</summary>
    public string? AirportCode { get; set; }

    /// <summary>IATA 三字代码（如 "PEK"）。</summary>
    public string? AirportIata { get; set; }

    /// <summary>ICAO 四字代码（如 "ZBAA"）。</summary>
    public string? AirportIcao { get; set; }

    /// <summary>机场显示名（如 "北京首都国际机场"）。</summary>
    public string? AirportName { get; set; }

    /// <summary>拍摄者备注。</summary>
    public string? Notes { get; set; }

    /// <summary>相机厂商。</summary>
    public string? CameraMake { get; set; }

    /// <summary>相机型号。</summary>
    public string? CameraModel { get; set; }

    /// <summary>镜头型号。</summary>
    public string? LensModel { get; set; }

    /// <summary>焦距（mm）。</summary>
    public double? FocalLength { get; set; }

    /// <summary>光圈值（如 f/5.6）。</summary>
    public string? Aperture { get; set; }

    /// <summary>快门速度（如 1/800s）。</summary>
    public string? ShutterSpeed { get; set; }

    /// <summary>ISO 值。</summary>
    public int? Iso { get; set; }

    /// <summary>GPS 纬度。</summary>
    public double? Latitude { get; set; }

    /// <summary>GPS 经度。</summary>
    public double? Longitude { get; set; }

    /// <summary>其余 EXIF 信息（JSON 字符串）。</summary>
    public string? ExifJson { get; set; }

    /// <summary>AI 识别状态：0=未识别；1=已识别；2=用户已修正。</summary>
    public int RecognitionStatus { get; set; }

    /// <summary>AI 识别出的机型（仅记录，不直接覆盖用户录入）。</summary>
    public string? RecognizedAircraftModel { get; set; }

    /// <summary>AI 识别置信度（0~1）。</summary>
    public double? RecognitionConfidence { get; set; }

    /// <summary>导入时间。</summary>
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.Now;
}
