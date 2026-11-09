using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AirPhotoGarage.Models;

namespace AirPhotoGarage.Services;

/// <summary>
/// 批量填写的字段集合。<b>null / 空白 = 不修改</b>该字段。
/// </summary>
public sealed record PhotoBatchFields(
    string? AircraftModel,
    string? RegistrationNumber,
    string? AirlineName,
    string? AirlineIata,
    string? AirportIata,
    string? AirportIcao,
    string? AirportName,
    string? FlightNumber,
    string? Notes);

public interface IPhotoBatchEditService
{
    /// <summary>把非空字段批量写入给定的照片，返回成功更新的张数。</summary>
    Task<int> ApplyAsync(IReadOnlyList<Photo> photos, PhotoBatchFields fields);
}

/// <summary>
/// 批量填写：只覆盖用户真正填了的字段，其余保持原样。
///
/// <para>
/// 「未填写机型」这类桶里的照片往往同属一架飞机 / 同一个航司，
/// 逐张点开填写太慢 —— 多选后一次写好是这些页面的主要操作。
/// </para>
/// </summary>
public sealed class PhotoBatchEditService : IPhotoBatchEditService
{
    private readonly IDatabaseService _db;

    public PhotoBatchEditService(IDatabaseService db) => _db = db;

    public async Task<int> ApplyAsync(IReadOnlyList<Photo> photos, PhotoBatchFields f)
    {
        if (photos.Count == 0) return 0;

        var model = Clean(f.AircraftModel);
        var registration = Clean(f.RegistrationNumber)?.ToUpperInvariant();
        var airlineName = Clean(f.AirlineName);
        var airlineIata = Clean(f.AirlineIata)?.ToUpperInvariant();
        var iata = Clean(f.AirportIata)?.ToUpperInvariant();
        var icao = Clean(f.AirportIcao)?.ToUpperInvariant();
        var airportName = Clean(f.AirportName);
        var flight = Clean(f.FlightNumber)?.ToUpperInvariant();
        var notes = Clean(f.Notes);

        var updated = 0;
        foreach (var photo in photos)
        {
            if (model is not null) photo.AircraftModel = model;
            if (registration is not null) photo.RegistrationNumber = registration;
            if (airlineName is not null) photo.AirlineName = airlineName;
            if (airlineIata is not null) photo.AirlineIata = airlineIata;
            if (iata is not null)
            {
                photo.AirportIata = iata;
                photo.AirportCode ??= iata;
            }
            if (icao is not null)
            {
                photo.AirportIcao = icao;
                photo.AirportCode ??= icao;
            }
            if (airportName is not null) photo.AirportName = airportName;
            if (flight is not null) photo.FlightNumber = flight;
            if (notes is not null) photo.Notes = notes;

            // 机型补上后，识别状态跟着更新，与手动编辑的规则一致
            if (model is not null && !string.IsNullOrWhiteSpace(photo.AircraftModel))
            {
                photo.RecognitionStatus = 2;
            }

            await _db.UpdatePhotoAsync(photo);
            updated++;
        }

        App.Log($"批量填写：{updated} 张（机型={model ?? "-"}, 注册号={registration ?? "-"}, 航司={airlineName ?? "-"}, 机场={iata ?? icao ?? "-"}）");
        return updated;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
