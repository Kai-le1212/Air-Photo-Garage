namespace AirPhotoGarage.Models;

/// <summary>
/// 机场条目（含 IATA / ICAO / 名称 / 坐标）。
/// 用于机场三字段联动与自动补全。
/// </summary>
public sealed class Airport
{
    public string? Iata { get; set; }
    public string? Icao { get; set; }
    public string Name { get; set; } = "";
    public string? City { get; set; }
    public string? Country { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
