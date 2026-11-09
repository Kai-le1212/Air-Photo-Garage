using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AirPhotoGarage.Models;

namespace AirPhotoGarage.Services;

/// <summary>补登记 + 去重的结果。</summary>
public sealed record LibraryRepairResult(
    int Scanned,
    int Unique,
    int Added,
    int AlreadyInDb,
    int DuplicateFilesSkipped,
    int DuplicateRowsRemoved,
    int ThumbnailsCreated,
    int Failed,
    IReadOnlyList<string> Errors)
{
    public string Summary
    {
        get
        {
            var parts = new List<string>
            {
                $"扫描 {Scanned} 个文件",
                $"其中 {Unique} 张是不同照片",
                $"补登记 {Added} 张",
            };
            if (DuplicateFilesSkipped > 0) parts.Add($"跳过重复副本 {DuplicateFilesSkipped} 个");
            if (DuplicateRowsRemoved > 0) parts.Add($"清掉重复记录 {DuplicateRowsRemoved} 条");
            if (ThumbnailsCreated > 0) parts.Add($"补生成缩略图 {ThumbnailsCreated} 张");
            if (Failed > 0) parts.Add($"失败 {Failed} 张");
            return string.Join("，", parts);
        }
    }
}

public interface ILibraryRepairService
{
    /// <summary>
    /// 扫描库目录：把「文件在、库里没有」的照片补登记回来，并按<b>文件内容哈希</b>去重
    /// （同一张图重复导入多次时只保留一条记录）。只新增与清理重复记录，
    /// <b>不删除磁盘上的照片文件</b>。
    /// </summary>
    Task<LibraryRepairResult> RepairAsync(Action<int, int>? progress = null, CancellationToken ct = default);
}

/// <summary>
/// 照片库修复：补登记 + 去重。
///
/// <para>
/// 背景：数据库被替换/重建过，而 <c>LibraryRoot\Photos</c> 里的副本还在；
/// 且同一张图被反复导入多次，磁盘上堆了成倍的副本。
/// </para>
///
/// <para>
/// 去重口径是<b>文件内容 SHA-256</b>：字节完全一致的才算重复。
/// （连拍/不同后期版本即使拍摄时间相同也会保留 —— 它们内容不同。）
/// 每组只保留一条记录，优先保留<b>已经入库</b>的那条，其次按文件名第一个。
/// </para>
///
/// <para>
/// 补登记复用与导入完全相同的元数据读取（EXIF + XMP 双源），拍摄时间、相机、
/// 镜头、光圈快门 ISO、GPS 都能还原；但<b>手填的机型/注册号/机场/航司/航班号/备注
/// 无法还原</b> —— 那些只存在数据库里，文件本身没有。
/// </para>
/// </summary>
public sealed class LibraryRepairService : ILibraryRepairService
{
    private static readonly string[] Extensions =
    {
        ".jpg", ".jpeg", ".png", ".heic", ".webp", ".tif", ".tiff", ".dng", ".raw",
    };

    private readonly IDatabaseService _db;
    private readonly IExifService _exif;
    private readonly string _photosRoot;
    private readonly string _thumbsRoot;

    public LibraryRepairService(IDatabaseService db, IExifService exif, string libraryRoot)
    {
        _db = db;
        _exif = exif;
        _photosRoot = Path.Combine(libraryRoot, "Photos");
        _thumbsRoot = Path.Combine(libraryRoot, "Thumbnails");
    }

    public async Task<LibraryRepairResult> RepairAsync(
        Action<int, int>? progress = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(_photosRoot))
        {
            return new LibraryRepairResult(0, 0, 0, 0, 0, 0, 0, 0,
                new[] { $"照片目录不存在：{_photosRoot}" });
        }

        var files = Directory
            .EnumerateFiles(_photosRoot, "*", SearchOption.AllDirectories)
            .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 现有记录：路径 → 记录（用于「优先保留已入库那条」和清理重复记录）
        var known = await _db.QueryPhotosAsync(new PhotoFilter(), 0, int.MaxValue);
        var byPath = new Dictionary<string, (long Id, string Path)>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in known) byPath[Normalize(p.FilePath)] = (p.Id, p.FilePath);

        // 1) 算哈希（分组键）
        var byHash = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var done = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            done++;
            progress?.Invoke(done, files.Count);

            try
            {
                var hash = await HashFileAsync(file, ct);
                if (!byHash.TryGetValue(hash, out var list))
                {
                    list = new List<string>();
                    byHash[hash] = list;
                }
                list.Add(file);
            }
            catch (Exception ex)
            {
                App.LogError($"Repair: 读取失败 {file}", ex);
            }
        }

        var unique = byHash.Count;
        var duplicateFiles = files.Count - unique;

        // 2) 每组留一条；另外把这一组里多余的「已入库」记录清掉
        int added = 0, alreadyInDb = 0, rowsRemoved = 0, thumbs = 0, failed = 0;
        var errors = new List<string>();
        var index = 0;

        foreach (var group in byHash.Values)
        {
            ct.ThrowIfCancellationRequested();
            index++;
            progress?.Invoke(index, unique);

            // 优先保留已入库的文件（避免无谓的增删）
            var keep = group.FirstOrDefault(f => byPath.ContainsKey(Normalize(f))) ?? group[0];

            // 同一组里其它已入库的记录 → 删掉（磁盘文件保留）
            foreach (var other in group)
            {
                var key = Normalize(other);
                if (ReferenceEquals(other, keep) || !byPath.TryGetValue(key, out var row)) continue;
                if (Normalize(keep) == key) continue;

                try
                {
                    await _db.DeletePhotoAsync(row.Id);
                    byPath.Remove(key);
                    rowsRemoved++;
                }
                catch (Exception ex)
                {
                    App.LogError($"Repair: 删除重复记录失败 {row.Path}", ex);
                }
            }

            if (byPath.ContainsKey(Normalize(keep)))
            {
                alreadyInDb++;
                continue;
            }

            try
            {
                var photo = await BuildPhotoAsync(keep, ct, () => thumbs++);
                await _db.InsertPhotoAsync(photo);
                byPath[Normalize(keep)] = (photo.Id, keep);
                added++;
            }
            catch (Exception ex)
            {
                failed++;
                if (errors.Count < 10) errors.Add($"{Path.GetFileName(keep)}：{ex.Message}");
                App.LogError($"Repair: 补登记失败 {keep}", ex);
            }
        }

        var result = new LibraryRepairResult(
            files.Count, unique, added, alreadyInDb, duplicateFiles,
            rowsRemoved, thumbs, failed, errors);

        App.Log("Repair 完成：" + result.Summary);
        return result;
    }

    private async Task<Photo> BuildPhotoAsync(string file, CancellationToken ct, Action onThumbCreated)
    {
        var info = new FileInfo(file);
        var exif = await _exif.ReadAsync(file);

        // 缩略图：预处理阶段已生成过同名文件，能复用；缺了才补
        var rel = Path.GetRelativePath(_photosRoot, file);
        var thumb = Path.Combine(_thumbsRoot, rel);
        if (!File.Exists(thumb))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(thumb)!);
            await ThumbnailGenerator.GenerateAsync(file, thumb, maxSide: 480, ct);
            onThumbCreated();
        }

        return new Photo
        {
            FilePath = file,
            ThumbnailPath = thumb,
            FileSize = info.Length,
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
            ExifJson = exif.RawDump,
            // 真实导入时间已随旧库丢失，用文件时间兜底，至少让「按导入时间排序」有意义
            ImportedAt = new DateTimeOffset(info.LastWriteTime),
        };
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var buffer = new byte[1 << 20];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            sha.TransformBlock(buffer, 0, read, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

    private static string? FormatAperture(double? fNumber) =>
        fNumber.HasValue ? $"f/{fNumber.Value:0.#}" : null;

    private static string? FormatShutter(double? seconds)
    {
        if (!seconds.HasValue || seconds.Value <= 0) return null;
        return seconds.Value >= 1
            ? $"{seconds.Value:0.#}s"
            : $"1/{Math.Round(1 / seconds.Value):0}s";
    }
}
