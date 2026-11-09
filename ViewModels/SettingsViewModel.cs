using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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

    // ---------- 照片库维护：补登记 + 去重 ----------

    /// <summary>修复进度 / 结果文本。</summary>
    [ObservableProperty]
    public partial string RepairStatus { get; set; } = "";

    [ObservableProperty]
    public partial bool IsRepairing { get; set; }

    /// <summary>修复按钮是否可点（修复中禁用，避免重复触发）。</summary>
    public bool CanRepair => !IsRepairing;

    partial void OnIsRepairingChanged(bool value) => OnPropertyChanged(nameof(CanRepair));

    /// <summary>
    /// 扫描库目录，把「文件在、库里没有」的照片补登记，并按内容哈希去掉重复记录。
    /// 磁盘上的照片文件<b>不会</b>被删除。
    /// </summary>
    [RelayCommand]
    private async Task RepairLibraryAsync()
    {
        if (IsRepairing) return;
        IsRepairing = true;
        RepairStatus = "正在扫描并计算哈希，照片多时需要一会儿…";
        try
        {
            var result = await App.LibraryRepair.RepairAsync(
                (done, total) => RepairStatus = $"处理中 {done}/{total}…");

            RepairStatus = result.Summary;
            if (result.Errors.Count > 0)
            {
                RepairStatus += Environment.NewLine + string.Join(Environment.NewLine, result.Errors);
            }

            // 照片墙立刻反映结果（InitializeAsync 内部就是 RefreshAsync）
            try { await App.GarageViewModel.InitializeAsync(); } catch { /* 刷新失败不影响结论 */ }
        }
        catch (Exception ex)
        {
            RepairStatus = "修复失败：" + ex.Message;
            App.LogError("RepairLibrary", ex);
        }
        finally
        {
            IsRepairing = false;
        }
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
