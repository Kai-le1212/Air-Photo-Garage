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

    /// <summary>
    /// 读到的<b>全部</b>元数据快照（扁平 JSON，键形如「Exif SubIFD · Lens Model」
    /// 或「XMP · exif:FNumber」），直接存进 <see cref="Photo.ExifJson"/>。
    ///
    /// <para>
    /// 为什么需要它：详情页的「原始 EXIF 数据」区块读的就是这一列，
    /// 而此前<b>从未有人给它赋值</b>，所以任何照片那里都是空的。
    /// </para>
    /// </summary>
    public string? RawDump { get; set; }
}
