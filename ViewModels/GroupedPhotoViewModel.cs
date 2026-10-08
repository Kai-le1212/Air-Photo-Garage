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
    private string _groupColumn;

    /// <summary>该页面展示用的标题，例如「机型」。</summary>
    private string _dimensionLabel;

    /// <summary>是否按天分成小分组（仅注册号页启用）。</summary>
    private bool _enableDayGrouping;

    public GroupedPhotoViewModel(IDatabaseService db, string groupColumn, string dimensionLabel,
        bool enableDayGrouping = false)
    {
        _db = db;
        _groupColumn = groupColumn;
        _dimensionLabel = dimensionLabel;
        _enableDayGrouping = enableDayGrouping;
    }

    /// <summary>
    /// 切换到另一个分组维度，复用同一个实例。
    ///
    /// <para>
    /// 为什么必须复用实例：页面的 <c>x:Bind</c>（OneWay）在初始化时订阅的是
    /// <b>当时的 ViewModel 实例</b>。若在 OnNavigatedTo 里换成新实例，
    /// 新实例发出的 PropertyChanged 根本到不了 UI，导致排序下拉框、
    /// 返回按钮等派生可见性属性永远停留在初始值。这里改为原地切换维度，
    /// 绑定关系保持不变。
    /// </para>
    /// </summary>
    public void SwitchDimension(string groupColumn, string dimensionLabel, bool enableDayGrouping)
    {
        if (_groupColumn == groupColumn && _dimensionLabel == dimensionLabel
            && _enableDayGrouping == enableDayGrouping)
        {
            return;
        }

        _groupColumn = groupColumn;
        _dimensionLabel = dimensionLabel;
        _enableDayGrouping = enableDayGrouping;

        // 清空上一维度的数据与状态，再刷新所有派生属性
        SelectedGroup = null;
        Groups.Clear();
        Photos.Clear();
        DayGroups.Clear();
        RaiseStateProperties();
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

    /// <summary>
    /// 统一重算所有「派生只读属性」的绑定。
    /// <para>
    /// 这些属性没有自己的 backing field，x:Bind 的 OneWay 无法自动感知变化，
    /// 必须在依赖状态（Groups / IsBusy / SelectedGroup / DayGroups…）变化后显式通知。
    /// 早期版本漏掉对 <see cref="EmptyHintVisibility"/> 的通知，导致有数据时
    /// 仍然显示「暂无XX数据」——这里集中处理，避免再次遗漏。
    /// </para>
    /// </summary>
    private void RaiseStateProperties()
    {
        OnPropertyChanged(nameof(IsViewingGroup));
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(GroupListVisibility));
        OnPropertyChanged(nameof(DetailVisibility));
        OnPropertyChanged(nameof(GroupListSortVisibility));
        OnPropertyChanged(nameof(PhotoSortVisibility));
        OnPropertyChanged(nameof(DayGroupsVisibility));
        OnPropertyChanged(nameof(FlatPhotosVisibility));
        OnPropertyChanged(nameof(EmptyHintVisibility));
        OnPropertyChanged(nameof(HasDayGroups));
    }

    public Microsoft.UI.Xaml.Visibility GroupListVisibility =>
        IsViewingGroup ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    /// <summary>列表态显示「分组排序」下拉。</summary>
    public Microsoft.UI.Xaml.Visibility GroupListSortVisibility =>
        IsViewingGroup ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    /// <summary>详情态显示「照片排序」下拉。</summary>
    public Microsoft.UI.Xaml.Visibility PhotoSortVisibility =>
        IsViewingGroup ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

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

    /// <summary>【详情态】分组内照片的排序方式。</summary>
    public IReadOnlyList<string> SortOptions { get; } = new[]
    {
        "拍摄时间（新→旧）", "拍摄时间（旧→新）", "导入时间（新→旧）",
        "机型 A→Z", "注册号 A→Z"
    };

    [ObservableProperty] public partial int SortIndex { get; set; }

    /// <summary>【列表态】分组本身的排序方式。</summary>
    public IReadOnlyList<string> GroupSortOptions { get; } = new[]
    {
        "照片数（多→少）", "照片数（少→多）", "分组名 A→Z",
        "首张拍摄时间（新→旧）", "首张拍摄时间（旧→新）"
    };

    [ObservableProperty] public partial int GroupSortIndex { get; set; }

    /// <summary>是否按天分成小分组（仅注册号页启用）。</summary>
    public bool EnableDayGrouping => _enableDayGrouping;

    partial void OnSortIndexChanged(int value)
    {
        if (IsViewingGroup) _ = LoadGroupPhotosAsync();
    }

    partial void OnGroupSortIndexChanged(int value)
    {
        // 列表态下切换排序 → 原地重排已加载的分组，无需重新查询数据库
        ApplyGroupSort();
    }

    /// <summary>按当前 GroupSortIndex 对 <see cref="Groups"/> 原地重排。</summary>
    private void ApplyGroupSort()
    {
        if (Groups.Count <= 1) return;

        IEnumerable<GroupItemViewModel> ordered = GroupSortIndex switch
        {
            1 => Groups.OrderBy(g => g.Count).ThenBy(g => g.Value, StringComparer.OrdinalIgnoreCase),
            2 => Groups.OrderBy(g => g.Value, StringComparer.OrdinalIgnoreCase),
            3 => Groups.OrderByDescending(g => g.FirstShotAt ?? DateTimeOffset.MinValue)
                       .ThenBy(g => g.Value, StringComparer.OrdinalIgnoreCase),
            4 => Groups.OrderBy(g => g.FirstShotAt ?? DateTimeOffset.MaxValue)
                       .ThenBy(g => g.Value, StringComparer.OrdinalIgnoreCase),
            _ => Groups.OrderByDescending(g => g.Count).ThenBy(g => g.Value, StringComparer.OrdinalIgnoreCase),
        };

        var sorted = ordered.ToList();
        // ObservableCollection 没有 Sort()，用 Move 原地重排以保留选中状态
        for (var target = 0; target < sorted.Count; target++)
        {
            var current = Groups.IndexOf(sorted[target]);
            if (current != target) Groups.Move(current, target);
        }
    }

    partial void OnSelectedGroupChanged(string? value)
    {
        OnPropertyChanged(nameof(IsViewingGroup));
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(GroupListVisibility));
        OnPropertyChanged(nameof(DetailVisibility));
        OnPropertyChanged(nameof(GroupListSortVisibility));
        OnPropertyChanged(nameof(PhotoSortVisibility));
    }

    // ---------- 加载 ----------

    [RelayCommand]
    public async Task LoadGroupsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        RaiseStateProperties();          // IsBusy 变化 → 空状态需重算
        StatusMessage = $"正在加载{_dimensionLabel}...";
        try
        {
            var counts = await _db.GetGroupCountsAsync(_groupColumn);
            Groups.Clear();
            foreach (var c in counts)
            {
                Groups.Add(new GroupItemViewModel(c, _dimensionLabel));
            }
            ApplyGroupSort();
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
            RaiseStateProperties();      // 关键：让 EmptyHintVisibility 跟随 Groups.Count 刷新
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
        DayGroups.Clear();
        RaiseStateProperties();
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

            var dayHint = EnableDayGrouping && DayGroups.Count > 0
                ? $"，按 {DayGroups.Count} 天分组"
                : string.Empty;
            StatusMessage = $"{SelectedGroup}：{items.Count} 张照片{dayHint}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
            RaiseStateProperties();
        }
    }

    // ---------- 按天分组（注册号页） ----------

    /// <summary>按天分组的小组集合。固定实例，切换维度时只清空内容。</summary>
    public ObservableCollection<DayGroup> DayGroups { get; } = new();

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
        RaiseStateProperties();
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

    /// <summary>该分组内最早的拍摄时间（用于列表页排序）。</summary>
    public DateTimeOffset? FirstShotAt => _count.FirstShotAt;

    /// <summary>该分组内最晚的拍摄时间。</summary>
    public DateTimeOffset? LastShotAt => _count.LastShotAt;

    /// <summary>照片张数，形如 "12 张"。</summary>
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
