using System;
using System.Collections.Generic;
using System.IO;

namespace AirPhotoGarage.Services;

/// <summary>列表的呈现方式。</summary>
public enum ViewKind
{
    /// <summary>条状：一行一个，缩略图在左、文字在右。</summary>
    List = 0,

    /// <summary>方块：网格排列，图片在上、文字在下。</summary>
    Grid = 1,
}

/// <summary>
/// 全局视图偏好（条状 / 方块），<b>按页面分别记忆</b>。
///
/// <para>
/// 每个页面各存一份：照片墙、机型页、机场页、注册号页互相独立 ——
/// 在机型页切成方块，不会把机场页也变成方块。
/// 机型页里的「注册号」中间层属于同一个页面，跟随机型页的设置。
/// </para>
///
/// <para>
/// 默认值按各页的「自然形态」给：分组列表一直是条状、照片墙分组一直是方块，
/// 这样装完不改任何设置也保持原有观感。
/// </para>
///
/// <para>
/// 存在 <c>%LocalAppData%\AirPhotoGarage\view-mode.txt</c>（<c>页=0/1</c> 每行一项）：
/// 不改 <c>settings.txt</c> —— 那个文件是「单值=int」的格式，主题读写会解析失败。
/// </para>
/// </summary>
public static class ViewModeStore
{
    public const string WallPage = "wall";
    public const string AircraftPage = "aircraft";
    public const string AirportPage = "airport";
    public const string RegistrationPage = "registration";

    /// <summary>旧版只有一个共用键，读到时摊给三个分组页，升级不丢已有偏好。</summary>
    private const string LegacyGroupKey = "group";

    private static readonly Dictionary<string, ViewKind> Modes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>任一项发生变化时触发，供各页面刷新派生可见性。</summary>
    public static event Action? Changed;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AirPhotoGarage", "view-mode.txt");

    /// <summary>
    /// 分组页的偏好键由分组列名决定 —— ViewModel 不用再额外传参。
    /// </summary>
    public static string PageKeyOf(string groupColumn) => groupColumn switch
    {
        "aircraft_model" => AircraftPage,
        "airport_icao" => AirportPage,
        "registration_number" => RegistrationPage,
        _ => groupColumn,
    };

    /// <summary>没存过的页用默认值：照片墙方块，其余条状。</summary>
    public static ViewKind DefaultOf(string pageKey) =>
        string.Equals(pageKey, WallPage, StringComparison.OrdinalIgnoreCase) ? ViewKind.Grid : ViewKind.List;

    public static ViewKind Get(string pageKey) =>
        Modes.TryGetValue(pageKey, out var kind) ? kind : DefaultOf(pageKey);

    public static void Set(string pageKey, ViewKind kind)
    {
        if (Get(pageKey) == kind) return;
        Modes[pageKey] = kind;
        Save();
        Changed?.Invoke();
    }

    /// <summary>启动时读一次。文件不存在或损坏就用默认值。</summary>
    public static void Load()
    {
        Modes.Clear();
        try
        {
            if (!File.Exists(FilePath)) return;

            var legacyGroup = (ViewKind?)null;

            foreach (var raw in File.ReadAllLines(FilePath))
            {
                var line = raw.Trim();
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = line[..eq].Trim();
                if (!int.TryParse(line[(eq + 1)..].Trim(), out var n)) continue;

                var kind = n == (int)ViewKind.Grid ? ViewKind.Grid : ViewKind.List;

                if (string.Equals(key, LegacyGroupKey, StringComparison.OrdinalIgnoreCase))
                {
                    legacyGroup = kind;
                }
                else if (key.Length > 0)
                {
                    Modes[key] = kind;
                }
            }

            // 旧格式 → 三个分组页都沿用当时的共用设置
            if (legacyGroup is { } g)
            {
                foreach (var page in new[] { AircraftPage, AirportPage, RegistrationPage })
                {
                    Modes.TryAdd(page, g);
                }
            }
        }
        catch { /* 读不出来就用默认值，不影响启动 */ }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            var lines = new List<string>();
            foreach (var page in new[] { WallPage, AircraftPage, AirportPage, RegistrationPage })
            {
                lines.Add($"{page}={(int)Get(page)}");
            }

            File.WriteAllLines(FilePath, lines);
        }
        catch { /* 存不下只影响下次启动的记忆 */ }
    }
}
