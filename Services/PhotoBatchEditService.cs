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

/// <summary>批量填写结果：真正改动了几张、几张内容本来就已经相同。</summary>
public sealed record PhotoBatchEditResult(int Updated, int Unchanged);

public interface IPhotoBatchEditService
{
    /// <summary>
    /// 把非空字段批量写入给定的照片。<b>值本来就相同的字段不会重写</b>，
    /// 因此「已经全部一样」的照片会原样跳过。
    /// </summary>
    Task<PhotoBatchEditResult> ApplyAsync(IReadOnlyList<Photo> photos, PhotoBatchFields fields);
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

    public async Task<PhotoBatchEditResult> ApplyAsync(
        IReadOnlyList<Photo> photos, PhotoBatchFields f)
    {
        if (photos.Count == 0) return new PhotoBatchEditResult(0, 0);

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
        var unchanged = 0;

        foreach (var photo in photos)
        {
            var changed = false;

            if (model is not null && Differs(photo.AircraftModel, model))
            {
                photo.AircraftModel = model;
                photo.RecognitionStatus = 2;
                changed = true;
            }

            if (registration is not null && Differs(photo.RegistrationNumber, registration))
            {
                photo.RegistrationNumber = registration;
                changed = true;
            }

            if (airlineName is not null && Differs(photo.AirlineName, airlineName))
            {
                photo.AirlineName = airlineName;
                changed = true;
            }

            if (airlineIata is not null && Differs(photo.AirlineIata, airlineIata))
            {
                photo.AirlineIata = airlineIata;
                changed = true;
            }

            if (iata is not null && Differs(photo.AirportIata, iata))
            {
                photo.AirportIata = iata;
                photo.AirportCode ??= iata;
                changed = true;
            }

            if (icao is not null && Differs(photo.AirportIcao, icao))
            {
                photo.AirportIcao = icao;
                photo.AirportCode ??= icao;
                changed = true;
            }

            if (airportName is not null && Differs(photo.AirportName, airportName))
            {
                photo.AirportName = airportName;
                changed = true;
            }

            if (flight is not null && Differs(photo.FlightNumber, flight))
            {
                photo.FlightNumber = flight;
                changed = true;
            }

            if (notes is not null && Differs(photo.Notes, notes))
            {
                photo.Notes = notes;
                changed = true;
            }

            if (!changed)
            {
                unchanged++;
                continue;
            }

            await _db.UpdatePhotoAsync(photo);
            updated++;
        }

        App.Log($"批量填写：更新 {updated} 张，内容已相同跳过 {unchanged} 张");
        return new PhotoBatchEditResult(updated, unchanged);
    }

    /// <summary>新值与库里的值是否有实质差异（忽略大小写与首尾空白）。</summary>
    private static bool Differs(string? current, string next) =>
        !string.Equals(current?.Trim(), next, StringComparison.OrdinalIgnoreCase);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
