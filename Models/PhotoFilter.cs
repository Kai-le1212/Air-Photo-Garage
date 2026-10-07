using System;

namespace AirPhotoGarage.Models;

/// <summary>
/// 照片筛选条件。所有字段均为可选；为 null/空 表示该维度不参与过滤。
/// </summary>
public sealed class PhotoFilter
{
    public string? Keyword { get; set; }
    public string? AircraftModel { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? AirportCode { get; set; }

    public DateTimeOffset? ShotFrom { get; set; }
    public DateTimeOffset? ShotTo { get; set; }

    /// <summary>排序方式：默认按拍摄时间倒序。</summary>
    public PhotoSortOrder SortOrder { get; set; } = PhotoSortOrder.ShotAtDescending;

    public PhotoFilter Clone() => new()
    {
        Keyword = Keyword,
        AircraftModel = AircraftModel,
        RegistrationNumber = RegistrationNumber,
        AirportCode = AirportCode,
        ShotFrom = ShotFrom,
        ShotTo = ShotTo,
        SortOrder = SortOrder,
    };
}

public enum PhotoSortOrder
{
    ShotAtDescending = 0,
    ShotAtAscending = 1,
    ImportedAtDescending = 2,
    AircraftModelAscending = 3,
    RegistrationAscending = 4,
}
