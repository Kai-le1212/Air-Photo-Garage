using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AirPhotoGarage.Models;
using AirPhotoGarage.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirPhotoGarage.ViewModels;

/// <summary>
/// 「按维度分组浏览」的通用 ViewModel，供「机型 / 机场 / 注册号」三个页面复用。
///
/// 交互模型：
/// 1. <b>分组列表</b>：列出该维度下的所有分组值 + 照片数（每个值一行）。
/// 2. 选中某个分组 → 展开该分组下的照片（可切换排序）。
/// 3. 带日期时，照片可按天分成"小分组"卡片。
/// </summary>
public sealed partial class GroupedPhotoViewModel : ObservableObject
{
    private readonly IDatabaseService _db;

    /// <summary>分组所依据的数据库列名（白名单内）。</summary>
    private readonly string _groupColumn;

    /// <summary>该页面展示用的标题，例如「机型」。</summary>
    private readonly string _dimensionLabel;

    public GroupedPhotoViewModel(IDatabaseService db, string groupColumn, string dimensionLabel)
    {
        _db = db;
        _groupColumn = groupColumn;
        _dimensionLabel = dimensionLabel;
    }

    // ---------- 状态 ----------

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string StatusMessage { get; set; } = "准备就绪";

    /// <summary>分组列表（每个分组值一行）。</summary>
    public ObservableCollection<GroupItemViewModel> Groups { get; } = new();

    /// <summary>当前选中分组下的照片。</summary>
    public ObservableCollection<PhotoCardViewModel> Photos { get; } = new();

    /// <summary>当前选中的分组值；null 表示未选中（显示分组列表）。</summary>
    [ObservableProperty] public partial string? SelectedGroup { get; set; }

    /// <summary>是否处于"查看某分组详情"状态。</summary>
    public bool IsViewingGroup => !string.IsNullOrEmpty(SelectedGroup);

    public string DimensionLabel => _dimensionLabel;

    /// <summary>分组依据的数据库列名（供页面判断是否需要重建 ViewModel）。</summary>
    public string GroupColumn => _groupColumn;

    /// <summary>页面标题：列表态显示维度名，详情态显示当前分组值。</summary>
    public string HeaderText => IsViewingGroup
        ? $"{_dimensionLabel} · {SelectedGroup}"
        : _dimensionLabel;

    // ---------- 可见性（XAML 直接绑定，避免转换器） ----------

    public Microsoft.UI.Xaml.Visibility GroupListVisibility =>
        IsViewingGroup ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    public Microsoft.UI.Xaml.Visibility DetailVisibility =>
        IsViewingGroup ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility DayGroupsVisibility =>
        HasDayGroups ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility FlatPhotosVisibility =>
        (!EnableDayGrouping || !HasDayGroups)
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility EmptyHintVisibility =>
        (Groups.Count == 0 && !IsBusy)
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>空状态提示文案。</summary>
    public string EmptyHint => $"暂无{_dimensionLabel}数据。请先导入照片并填写{_dimensionLabel}信息。";

    // ---------- 排序 ----------

    public IReadOnlyList<string> SortOptions { get; } = new[]
    {
        "拍摄时间（新→旧）", "拍摄时间（旧→新）", "导入时间（新→旧）",
        "机型 A→Z", "注册号 A→Z"
    };

    [ObservableProperty] public partial int SortIndex { get; set; }

    /// <summary>是否按天分成小分组（仅注册号页启用）。</summary>
    public bool EnableDayGrouping { get; init; }

    partial void OnSortIndexChanged(int value)
    {
        if (IsViewingGroup) _ = LoadGroupPhotosAsync();
    }

    partial void OnSelectedGroupChanged(string? value)
    {
        OnPropertyChanged(nameof(IsViewingGroup));
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(GroupListVisibility));
        OnPropertyChanged(nameof(DetailVisibility));
    }

    // ---------- 加载 ----------

    [RelayCommand]
    public async Task LoadGroupsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = $"正在加载{_dimensionLabel}...";
        try
        {
            var counts = await _db.GetGroupCountsAsync(_groupColumn);
            Groups.Clear();
            foreach (var c in counts)
            {
                Groups.Add(new GroupItemViewModel(c, _dimensionLabel));
            }
            StatusMessage = counts.Count == 0
                ? $"暂无{_dimensionLabel}数据（先导入照片并填写{_dimensionLabel}）"
                : $"共 {counts.Count} 个{_dimensionLabel}";
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

    /// <summary>进入某个分组的详情。</summary>
    [RelayCommand]
    public async Task OpenGroupAsync(GroupItemViewModel? group)
    {
        if (group is null) return;
        SelectedGroup = group.Value;
        await LoadGroupPhotosAsync();
    }

    /// <summary>返回分组列表。</summary>
    [RelayCommand]
    public void BackToGroups()
    {
        SelectedGroup = null;
        Photos.Clear();
        _dayGroups.Clear();
        OnPropertyChanged(nameof(DayGroups));
        OnPropertyChanged(nameof(HasDayGroups));
    }

    private async Task LoadGroupPhotosAsync()
    {
        if (string.IsNullOrEmpty(SelectedGroup)) return;
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var order = SortIndex switch
            {
                1 => PhotoSortOrder.ShotAtAscending,
                2 => PhotoSortOrder.ImportedAtDescending,
                3 => PhotoSortOrder.AircraftModelAscending,
                4 => PhotoSortOrder.RegistrationAscending,
                _ => PhotoSortOrder.ShotAtDescending,
            };
            var items = await _db.GetPhotosByColumnValueAsync(_groupColumn, SelectedGroup, order);

            Photos.Clear();
            foreach (var p in items)
            {
                Photos.Add(new PhotoCardViewModel(p, App.UiDispatcher));
            }

            BuildDayGroups(items);

            StatusMessage = $"{SelectedGroup}：{items.Count} 张照片";
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

    // ---------- 按天分组（注册号页） ----------

    private readonly List<DayGroup> _dayGroups = new();
    private ObservableCollection<DayGroup>? _dayGroupsObservable;

    public ObservableCollection<DayGroup> DayGroups => _dayGroupsObservable ??= new();

    public bool HasDayGroups => EnableDayGrouping && DayGroups.Count > 0;

    private void BuildDayGroups(IReadOnlyList<Photo> photos)
    {
        DayGroups.Clear();
        if (!EnableDayGrouping) return;

        // 同一天拍摄的照片归为一小组；按日期倒序
        var byDay = photos
            .Where(p => p.ShotAt.HasValue)
            .GroupBy(p => p.ShotAt!.Value.ToLocalTime().Date)
            .OrderByDescending(g => g.Key);

        foreach (var g in byDay)
        {
            var cards = g.Select(p => new PhotoCardViewModel(p, App.UiDispatcher)).ToList();
            DayGroups.Add(new DayGroup(g.Key, cards));
        }
        OnPropertyChanged(nameof(HasDayGroups));
    }
}

/// <summary>分组列表中的一行。</summary>
public sealed class GroupItemViewModel
{
    private readonly GroupCount _count;

    public GroupItemViewModel(GroupCount count, string dimensionLabel)
    {
        _count = count;
        DimensionLabel = dimensionLabel;
    }

    public string Value => _count.Value;
    public int Count => _count.Count;
    public string DimensionLabel { get; }

    public string CountText => $"{_count.Count} 张";

    /// <summary>拍摄时间范围摘要，例如 "2024-03 ~ 2025-01"。</summary>
    public string RangeText =>
        _count.FirstShotAt.HasValue && _count.LastShotAt.HasValue
            ? $"{_count.FirstShotAt.Value.ToLocalTime():yyyy-MM-dd} ~ {_count.LastShotAt.Value.ToLocalTime():yyyy-MM-dd}"
            : "无拍摄时间";
}

/// <summary>按天分组的小组（用于注册号页）。</summary>
public sealed class DayGroup
{
    public DayGroup(DateTime date, IReadOnlyList<PhotoCardViewModel> photos)
    {
        Date = date;
        Photos = photos;
    }

    public DateTime Date { get; }
    public IReadOnlyList<PhotoCardViewModel> Photos { get; }
    public string Title => Date.ToString("yyyy-MM-dd");
    public string CountText => $"{Photos.Count} 张";
}
