using System;
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
/// 全局视图偏好（条状 / 方块）。
///
/// <para>
/// <b>分成两个独立的开关</b>，而不是一个：两个位置的「自然形态」本来就不同 ——
/// 分组列表一直是条状、照片墙的注册号分组一直是方块。
/// 合成一个开关的话，默认值必然会把其中一边的现有观感改掉。
/// </para>
///
/// <para>
/// 存在 <c>%LocalAppData%\AirPhotoGarage\view-mode.txt</c>（键值行）：
/// 不改 <c>settings.txt</c> —— 那个文件是「单值=int」的格式，主题读写会解析失败。
/// </para>
/// </summary>
public static class ViewModeStore
{
    private const string GroupListKey = "group";
    private const string WallKey = "wall";

    /// <summary>机型 / 机场 / 注册号的分组列表（含机型页里的注册号层）。默认条状。</summary>
    public static ViewKind GroupList { get; private set; } = ViewKind.List;

    /// <summary>照片墙按注册号分组后的呈现。默认方块。</summary>
    public static ViewKind Wall { get; private set; } = ViewKind.Grid;

    /// <summary>任一项发生变化时触发，供各页面刷新派生可见性。</summary>
    public static event Action? Changed;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AirPhotoGarage", "view-mode.txt");

    public static void SetGroupList(ViewKind kind)
    {
        if (GroupList == kind) return;
        GroupList = kind;
        Save();
        Changed?.Invoke();
    }

    public static void SetWall(ViewKind kind)
    {
        if (Wall == kind) return;
        Wall = kind;
        Save();
        Changed?.Invoke();
    }

    /// <summary>启动时读一次。文件不存在或损坏就用默认值。</summary>
    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            foreach (var raw in File.ReadAllLines(FilePath))
            {
                var line = raw.Trim();
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();
                if (!int.TryParse(value, out var n)) continue;

                var kind = n == (int)ViewKind.Grid ? ViewKind.Grid : ViewKind.List;
                if (string.Equals(key, GroupListKey, StringComparison.OrdinalIgnoreCase)) GroupList = kind;
                else if (string.Equals(key, WallKey, StringComparison.OrdinalIgnoreCase)) Wall = kind;
            }
        }
        catch { /* 读不出来就用默认值，不影响启动 */ }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath,
                $"{GroupListKey}={(int)GroupList}{Environment.NewLine}" +
                $"{WallKey}={(int)Wall}{Environment.NewLine}");
        }
        catch { /* 存不下只影响下次启动的默认值 */ }
    }
}
