using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AirPhotoGarage.Services;

/// <summary>
/// 分组头图（机型 / 注册号 / 机场）。
///
/// <para>
/// <b>不碰数据库</b>：映射写在库根目录的 <c>covers.json</c> 里，
/// 选中的图片会被规范化成小尺寸 PNG 复制到 <c>Covers/</c> 目录。
/// 这样既不会往 <c>photos</c> 表里塞假记录，也不会因为原照片被删/被移动而丢头图。
/// </para>
///
/// <para>
/// 应用启动时一次性读进内存，之后读操作走内存字典；写操作加锁并<b>原子落盘</b>
/// （先写 .tmp 再 Move 覆盖），避免中途崩溃把 json 写成半截。
/// </para>
/// </summary>
public interface ICoverService
{
    /// <summary>该组的自定义头图本地副本路径；未自定义时返回 null。</summary>
    string? GetCustomCoverPath(string dimension, string? value);

    /// <summary>该组是否设置过自定义头图。</summary>
    bool HasCustomCover(string dimension, string? value);

    /// <summary>
    /// 把外部图片设为该组的头图：规范化后复制进 <c>Covers/</c> 并写入映射。
    /// 返回副本路径；读取/编码失败时返回 null（调用方负责提示）。
    /// </summary>
    Task<string?> SetFromFileAsync(string dimension, string? value, string sourcePath,
        CancellationToken cancellationToken = default);

    /// <summary>取消自定义头图，回到「组内最新一张」的自动封面。</summary>
    Task ClearAsync(string dimension, string? value);
}

public sealed class CoverService : ICoverService
{
    /// <summary>头图副本的最长边。行内缩略图只有 96px 宽、窗格封面 240px 宽，512 足够且省空间。</summary>
    private const int CoverMaxSide = 512;

    private readonly string _jsonPath;
    private readonly string _coversDir;
    private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public CoverService(string libraryRoot)
    {
        _jsonPath = Path.Combine(libraryRoot, "covers.json");
        _coversDir = Path.Combine(libraryRoot, "Covers");
        Load();
    }

    public string? GetCustomCoverPath(string dimension, string? value)
    {
        lock (_gate)
        {
            return _map.TryGetValue(KeyOf(dimension, value), out var path) && File.Exists(path)
                ? path
                : null;
        }
    }

    public bool HasCustomCover(string dimension, string? value)
    {
        lock (_gate)
        {
            return _map.ContainsKey(KeyOf(dimension, value));
        }
    }

    public async Task<string?> SetFromFileAsync(string dimension, string? value, string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return null;

        Directory.CreateDirectory(_coversDir);

        var key = KeyOf(dimension, value);
        var safeKey = Sanitize(key);
        if (safeKey.Length > 60) safeKey = safeKey[..60];   // 别让文件名顶到路径长度上限
        var destPath = Path.Combine(_coversDir, $"{safeKey}_{Guid.NewGuid():N}.png");

        try
        {
            // 走和缩略图同一条链路：保证产物一定可解码、体积可控，
            // 也顺手把超大图/异常格式挡在这里，而不是让界面显示一片空白。
            await ThumbnailGenerator.GenerateAsync(sourcePath, destPath, CoverMaxSide, cancellationToken);
        }
        catch
        {
            try { if (File.Exists(destPath)) File.Delete(destPath); } catch { }
            return null;
        }

        string? oldPath;
        lock (_gate)
        {
            _map.TryGetValue(key, out oldPath);
            _map[key] = destPath;
            Save();
        }

        DeleteOwnedCopy(oldPath, destPath);
        return destPath;
    }

    public Task ClearAsync(string dimension, string? value)
    {
        string? oldPath;
        lock (_gate)
        {
            var key = KeyOf(dimension, value);
            if (!_map.TryGetValue(key, out oldPath)) return Task.CompletedTask;
            _map.Remove(key);
            Save();
        }

        DeleteOwnedCopy(oldPath, null);
        return Task.CompletedTask;
    }

    /// <summary>删掉旧的自有副本 —— 只删 <c>Covers/</c> 目录里、且不是新目标的那一份。</summary>
    private void DeleteOwnedCopy(string? path, string? keepPath)
    {
        if (string.IsNullOrEmpty(path) || path == keepPath) return;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (dir is null) return;
            if (!string.Equals(Path.GetFullPath(dir), Path.GetFullPath(_coversDir),
                    StringComparison.OrdinalIgnoreCase))
            {
                return;   // 不是我们生成的副本，绝不动它
            }
            if (File.Exists(path)) File.Delete(path);
        }
        catch { /* 删不掉只是留个孤儿文件，不影响功能 */ }
    }

    /// <summary>映射的键：维度 + 分组值。<c>null</c>（未填写桶）按空串处理。</summary>
    private static string KeyOf(string dimension, string? value) => $"{dimension}|{value ?? string.Empty}";

    /// <summary>把它变成安全的文件名片段：非法字符换成下划线。</summary>
    private static string Sanitize(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = raw.Select(c => invalid.Contains(c) || c == '|' ? '_' : c).ToArray();
        var s = new string(chars).Trim();
        return s.Length == 0 ? "cover" : s;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_jsonPath)) return;
            var json = File.ReadAllText(_jsonPath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict is null) return;
            lock (_gate)
            {
                _map.Clear();
                foreach (var kv in dict) _map[kv.Key] = kv.Value;
            }
        }
        catch { /* json 损坏就当作没有自定义头图，不阻塞启动 */ }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_jsonPath)!);
            var json = JsonSerializer.Serialize(_map, new JsonSerializerOptions { WriteIndented = true });
            var tmp = _jsonPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _jsonPath, overwrite: true);
        }
        catch { /* 写盘失败只影响本次设置 */ }
    }
}
