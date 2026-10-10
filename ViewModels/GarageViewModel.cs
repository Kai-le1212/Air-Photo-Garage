using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AirPhotoGarage.Models;
using AirPhotoGarage.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirPhotoGarage.ViewModels;

/// <summary>
/// 照片墙主视图模型。
/// 负责：维护筛选条件、刷新照片列表、协调导入流程。
/// 所有数据库/IO 调用均在后台线程，避免阻塞 UI。
/// </summary>
public sealed partial class GarageViewModel : ObservableObject
{
    private readonly IDatabaseService _db;
    private readonly IPhotoImportService _importer;
    private readonly IUiDispatcher _ui;

    public GarageViewModel(IDatabaseService db, IPhotoImportService importer, IUiDispatcher uiDispatcher)
    {
        _db = db;
        _importer = importer;
        _ui = uiDispatcher;
        Photos = new ObservableCollection<PhotoCardViewModel>();
    }

    public ObservableCollection<PhotoCardViewModel> Photos { get; }

    /// <summary>
    /// 照片墙的「按注册号」分组视图。注册号才唯一标识一架飞机，
    /// 平铺视图下同型机混在一起难以对号入座。
    /// </summary>
    public ObservableCollection<RegistrationGroup> RegistrationGroups { get; } = new();

    /// <summary>是否按注册号分组显示照片墙（默认开）。关掉即回到平铺视图。</summary>
    [ObservableProperty] public partial bool GroupByRegistration { get; set; } = true;

    partial void OnGroupByRegistrationChanged(bool value)
    {
        if (value) BuildRegistrationGroups();
        RaisePhotoViewProperties();
    }

    /// <summary>分组视图可见性：开关打开且确实分出了组。</summary>
    public Microsoft.UI.Xaml.Visibility GroupedWallVisibility =>
        (GroupByRegistration && RegistrationGroups.Count > 0)
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>平铺视图可见性（与分组视图互斥）。</summary>
    public Microsoft.UI.Xaml.Visibility FlatWallVisibility =>
        (!GroupByRegistration || RegistrationGroups.Count == 0)
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    private void RaisePhotoViewProperties()
    {
        OnPropertyChanged(nameof(GroupedWallVisibility));
        OnPropertyChanged(nameof(FlatWallVisibility));
    }

    /// <summary>
    /// 按注册号把已加载的照片切片。
    ///
    /// <para>
    /// 组的顺序<b>不</b>单独排序，而是沿用该组第一张照片在 <see cref="Photos"/> 里的位置 ——
    /// 这样组序会自动跟随筛选面板里的「排序方式」，不需要再维护第二套排序规则。
    /// </para>
    /// </summary>
    private void BuildRegistrationGroups()
    {
        RegistrationGroups.Clear();

        if (!GroupByRegistration || Photos.Count == 0)
        {
            RaisePhotoViewProperties();
            return;
        }

        var order = new Dictionary<long, int>(Photos.Count);
        for (var i = 0; i < Photos.Count; i++) order[Photos[i].Id] = i;

        var grouped = Photos
            .GroupBy(c => string.IsNullOrWhiteSpace(c.Photo.RegistrationNumber)
                ? null
                : c.Photo.RegistrationNumber!.Trim())
            .OrderBy(g => g.Min(c => order[c.Id]))
            .ToList();

        foreach (var g in grouped)
        {
            RegistrationGroups.Add(new RegistrationGroup(g.Key, g.ToList()));
        }

        RaisePhotoViewProperties();
    }

    // ---- 筛选条件（双向绑定） ----

    [ObservableProperty] public partial string? Keyword { get; set; }
    [ObservableProperty] public partial string? AircraftModelFilter { get; set; }
    [ObservableProperty] public partial string? RegistrationFilter { get; set; }
    [ObservableProperty] public partial string? AirportCodeFilter { get; set; }
    [ObservableProperty] public partial DateTimeOffset? ShotFrom { get; set; }
    [ObservableProperty] public partial DateTimeOffset? ShotTo { get; set; }

    public IReadOnlyList<string> SortOptions { get; } = new[]
    {
        "拍摄时间倒序", "拍摄时间正序", "导入时间倒序", "机型 A→Z", "注册号 A→Z"
    };

    [ObservableProperty] public partial int SortIndex { get; set; }

    [ObservableProperty] public partial string StatusMessage { get; set; } = "准备就绪";
    [ObservableProperty] public partial bool IsBusy { get; set; }

    // ---- 自动补全候选 ----
    public ObservableCollection<string> AircraftSuggestions { get; } = new();
    public ObservableCollection<string> RegistrationSuggestions { get; } = new();
    public ObservableCollection<string> AirportSuggestions { get; } = new();

    partial void OnSortIndexChanged(int value) => _ = RefreshAsync();

    partial void OnKeywordChanged(string? value) { /* 由 ApplyFilter 统一触发 */ }
    partial void OnAircraftModelFilterChanged(string? value) { }
    partial void OnRegistrationFilterChanged(string? value) { }
    partial void OnAirportCodeFilterChanged(string? value) { }
    partial void OnShotFromChanged(DateTimeOffset? value) { }
    partial void OnShotToChanged(DateTimeOffset? value) { }

    public void InvalidateFilter() => _ = RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "正在加载...";
        try
        {
            var filter = BuildFilter();
            var items = await _db.QueryPhotosAsync(filter, skip: 0, take: 5000);

            Photos.Clear();
            foreach (var p in items)
            {
                Photos.Add(new PhotoCardViewModel(p, _ui));
            }
            BuildRegistrationGroups();

            StatusMessage = GroupByRegistration && RegistrationGroups.Count > 0
                ? $"共 {Photos.Count} 张照片 · {RegistrationGroups.Count} 架飞机"
                : $"共 {Photos.Count} 张照片";

            await RefreshSuggestionsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ImportPhotosAsync(IReadOnlyList<string> filePaths)
    {
        if (filePaths is null || filePaths.Count == 0) return;
        if (IsBusy) return;

        // 1. 打开导入向导（预处理照片），并等待用户在界面上完成或取消
        var wizard = App.ImportWizardViewModel;
        var started = await wizard.StartAsync(filePaths);
        if (!started) return;

        var confirmed = await wizard.WaitForCompletionAsync();
        if (!confirmed || wizard.CompletedPhotos.Count == 0) return;

        // 2. 用户确认后，把填好元数据的照片写入数据库
        IsBusy = true;
        var imported = 0;
        var failed = 0;
        StatusMessage = $"正在写入数据库...";
        try
        {
            foreach (var photo in wizard.CompletedPhotos)
            {
                try
                {
                    photo.RecognitionStatus = string.IsNullOrWhiteSpace(photo.AircraftModel) ? 0 : 2;
                    await _db.InsertPhotoAsync(photo);
                    imported++;
                }
                catch (Exception ex)
                {
                    failed++;
                    System.Diagnostics.Debug.WriteLine($"DB insert failed: {ex}");
                }
            }
            StatusMessage = $"导入完成：成功 {imported} 张，失败 {failed} 张";
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 把已从数据库删除的照片从视图集合里移除，并重建注册号分组。
    ///
    /// <para>
    /// 删除的<b>确认与落库</b>统一由 <see cref="Views.PhotoDeleteDialog"/> 负责
    /// （照片墙与三个分组页共用），这里只管刷新视图 ——
    /// 职责分开之后，两边的删除入口才能走同一套逻辑，不会再出现
    /// 「照片墙能删、分组页不能删」这种能力漂移。
    /// </para>
    /// </summary>
    public void RemoveDeletedPhoto(PhotoCardViewModel card)
    {
        if (card is null) return;
        Photos.Remove(card);
        BuildRegistrationGroups();   // 卡片同时存在于注册号分组视图里，必须一起重建
        StatusMessage = $"已删除照片 #{card.Id}";
    }

    [RelayCommand]
    private void ClearFilter()
    {
        Keyword = null;
        AircraftModelFilter = null;
        RegistrationFilter = null;
        AirportCodeFilter = null;
        ShotFrom = null;
        ShotTo = null;
        SortIndex = 0;
        _ = RefreshAsync();
    }

    /// <summary>
    /// 由 View 在 Loaded 后调用一次，触发首屏数据加载。
    /// </summary>
    public Task InitializeAsync() => RefreshAsync();

    private PhotoFilter BuildFilter()
    {
        var order = SortIndex switch
        {
            1 => PhotoSortOrder.ShotAtAscending,
            2 => PhotoSortOrder.ImportedAtDescending,
            3 => PhotoSortOrder.AircraftModelAscending,
            4 => PhotoSortOrder.RegistrationAscending,
            _ => PhotoSortOrder.ShotAtDescending,
        };
        return new PhotoFilter
        {
            Keyword = Keyword,
            AircraftModel = AircraftModelFilter,
            RegistrationNumber = RegistrationFilter,
            AirportCode = AirportCodeFilter,
            ShotFrom = ShotFrom,
            ShotTo = ShotTo,
            SortOrder = order,
        };
    }

    private async Task RefreshSuggestionsAsync()
    {
        try
        {
            var aircraft = await _db.GetDistinctValuesAsync("aircraft_model");
            var regs = await _db.GetDistinctValuesAsync("registration_number");
            var airports = await _db.GetDistinctValuesAsync("airport_code");
            _ui.TryEnqueue(() =>
            {
                AircraftSuggestions.Clear();
                foreach (var a in aircraft) AircraftSuggestions.Add(a);
                RegistrationSuggestions.Clear();
                foreach (var r in regs) RegistrationSuggestions.Add(r);
                AirportSuggestions.Clear();
                foreach (var ap in airports) AirportSuggestions.Add(ap);
            });
        }
        catch
        {
            // ignore
        }
    }
}

/// <summary>
/// 照片墙上的一个「按注册号」分组 —— 在界面上表现为<b>一个窗格</b>（封面 + 张数），
/// 点开后在弹出面板里看该注册号的全部照片。
/// </summary>
public sealed class RegistrationGroup : ObservableObject
{
    private readonly PhotoCardViewModel? _cover;

    public RegistrationGroup(string? registration, IReadOnlyList<PhotoCardViewModel> photos)
    {
        Registration = registration;
        Photos = photos;
        _cover = photos.Count > 0 ? photos[0] : null;

        // 封面缩略图是构造后异步加载的。不转发这个通知，窗格上的封面会一直空着 ——
        // x:Bind 无从得知 Thumbnail 已经就绪。
        if (_cover is not null)
        {
            _cover.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PhotoCardViewModel.Thumbnail))
                {
                    OnPropertyChanged(nameof(CoverThumbnail));
                }
            };
        }
    }

    /// <summary>注册号；<c>null</c> 表示这是「未填写注册号」的兜底分组。</summary>
    public string? Registration { get; }

    /// <summary>组内照片，顺序沿用照片墙当前的排序。</summary>
    public IReadOnlyList<PhotoCardViewModel> Photos { get; }

    /// <summary>窗格封面：取组内第一张的缩略图。</summary>
    public Microsoft.UI.Xaml.Media.ImageSource? CoverThumbnail => _cover?.Thumbnail;

    public string Title => Registration ?? "未填写注册号";

    public string CountText => $"{Photos.Count} 张";

    /// <summary>「未填写注册号」徽标可见性。</summary>
    public Microsoft.UI.Xaml.Visibility MissingBadgeVisibility =>
        Registration is null
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>
    /// 该注册号下出现过的机型。同一架飞机可能换装/重录机型，故用「/」连接去重后的多个机型。
    /// </summary>
    public string ModelsText
    {
        get
        {
            var models = Photos
                .Select(c => c.Photo.AircraftModel)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return models.Count == 0 ? "未填写机型" : string.Join(" / ", models);
        }
    }

    /// <summary>组内拍摄时间范围，例如 "2021-05-01 ~ 2026-10-02"。</summary>
    public string RangeText
    {
        get
        {
            var times = Photos
                .Where(c => c.Photo.ShotAt.HasValue)
                .Select(c => c.Photo.ShotAt!.Value)
                .OrderBy(t => t)
                .ToList();
            if (times.Count == 0) return "无拍摄时间";
            // ShotAt 存的是相机本地时间（EXIF 无时区，按 UTC+0 原样保存），不可再 ToLocalTime
            return $"{times[0]:yyyy-MM-dd} ~ {times[^1]:yyyy-MM-dd}";
        }
    }

    /// <summary>组副标题：机型 · 时间范围 · 机场。</summary>
    public string SummaryText => $"{ModelsText}  ·  {RangeText}  ·  {AirportsText}";

    /// <summary>出现过的机场数（去重），用于一句话概括这架飞机的拍摄足迹。</summary>
    public string AirportsText
    {
        get
        {
            var airports = Photos
                .Select(c => c.Photo.AirportIata ?? c.Photo.AirportIcao ?? c.Photo.AirportCode)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return airports.Count == 0 ? "未填写机场" : string.Join(" / ", airports);
        }
    }
}
