using System;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AirPhotoGarage.ViewModels;

/// <summary>
/// 设置页 ViewModel。
/// 按 winui3-full-skill 规则：ViewModel 不直接引用 Microsoft.UI.Xaml.*，
/// 主题切换通过 <see cref="ApplyTheme"/> 静态回调委托给 App 层。
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>主题索引对应的选项（与 UI RadioButtons 顺序一致）。</summary>
    public enum ThemeChoice
    {
        SystemDefault = 0,
        Light = 1,
        Dark = 2,
    }

    /// <summary>App 层注入的主题应用回调，签名：int -> void（0/1/2 = System/Light/Dark）。</summary>
    public static Action<int>? ApplyTheme;

    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    public SettingsViewModel()
    {
        // 初始化时直接写属性：ApplyTheme 尚未注入（null，被 ?. 跳过），
        // SaveToDisk 幂等写出同一值，无副作用。
        ThemeIndex = LoadFromDisk();
    }

    partial void OnThemeIndexChanged(int value)
    {
        ApplyTheme?.Invoke(value);
        SaveToDisk(value);
    }

    /// <summary>
    /// 版本号文本，例如 "v0.2.2.3"。
    ///
    /// <para>
    /// ⚠️ 必须包含第<b>四</b>位（Revision）。本项目用第四位区分「无新增功能」的迭代
    /// （有新增功能升第三位、无新增功能升第四位），此前只取 Major.Minor.Build，
    /// 结果 0.2.2.0 / 0.2.2.1 / 0.2.2.2 / 0.2.2.3 在界面上全都显示成「v0.2.2」，
    /// 根本分不出装的是哪一版。
    /// </para>
    /// </summary>
    public string VersionText
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "未知版本" : $"v{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
        }
    }

    /// <summary>版权信息文本（多行）。</summary>
    public string CopyrightText =>
        "Air Photo Garage · 航空摄影整理工具\n" +
        "© 2026 AirPhotoGarage Team\n" +
        "使用 .NET 10 + Windows App SDK 2.3 + WinUI 3 + SQLite 构建";

    public string DatabasePathText => App.DatabasePath;

    public string LibraryRootText => App.LibraryRoot;

    /// <summary>
    /// 由设置页在用户选定新库目录后调用：把路径写入配置。
    /// 运行时实例（Database/Importer）不在此切换，需重启应用生效。
    /// </summary>
    public void SetLibraryRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        App.SaveLibraryRoot(path);
        OnPropertyChanged(nameof(LibraryRootText));
        OnPropertyChanged(nameof(DatabasePathText));
    }

    /// <summary>判断给定路径是否与当前库目录一致。</summary>
    public bool IsCurrentLibraryRoot(string path) =>
        string.Equals(
            Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(App.LibraryRoot).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    // ---------- 持久化 ----------

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AirPhotoGarage", "settings.txt");

    private static int LoadFromDisk()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = File.ReadAllText(SettingsPath).Trim();
                if (int.TryParse(s, out var i) && i >= 0 && i <= 2) return i;
            }
        }
        catch { }
        return 0;
    }

    private static void SaveToDisk(int value)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, value.ToString());
        }
        catch { }
    }
}
