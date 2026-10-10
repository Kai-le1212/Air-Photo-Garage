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
/// 3. 注册号维度额外启用<b>二层分组</b>（<see cref="EnableDayGrouping"/>）：
///    详情以<b>时间轴</b>呈现，节点分两类 ——
///    <list type="bullet">
///      <item><b>自定义分组</b>：用户右键「加入分组…」手动建立并指派成员，可命名（如「2024 珠海航展」）。</item>
///      <item><b>自动按天</b>：未加入任何自定义分组的照片，按拍摄日期自动成组。</item>
///    </list>
///    两类节点<b>互斥</b>：一张照片至多归属一个自定义分组，命中后即从按天节点中移出，
///    因此每张照片在时间轴上只出现一次。
/// </summary>
public sealed partial class GroupedPhotoViewModel : ObservableObject
{
    private readonly IDatabaseService _db;

    /// <summary>分组所依据的数据库列名（白名单内）。</summary>
    private string _groupColumn;

    /// <summary>该页面展示用的标题，例如「机型」。</summary>
    private string _dimensionLabel;

    /// <summary>是否启用时间轴（按天自动分组）。机场页与注册号页为 true。</summary>
    private bool _enableDayGrouping;

    /// <summary>
    /// 是否在分组详情里再插入一层「注册号」。
    /// 机型页为 true（机型 → 注册号 → 照片）：机型是<b>共享</b>的，
    /// 必须先落到具体哪一架飞机才有意义。
    /// </summary>
    private bool _enableRegistrationSubLevel;

    /// <summary>
    /// 是否允许用户自定义分组。仅注册号维度为 true ——
    /// 自定义分组绑定了注册号，跨飞机合并分组没有意义。
    /// </summary>
    private bool _enableUserGroups;

    /// <summary>外层分组行额外展示的列（机场页 = [机场名, IATA]）。</summary>
    private IReadOnlyList<string> _rowLabelColumns = Array.Empty<string>();

    /// <summary>
    /// 外层分组行的<b>小字附件</b>列（注册号页 = [机型]）。
    /// 与 <see cref="_rowLabelColumns"/> 的区别是进标题还是进小字：
    /// 注册号行的标题就是注册号本身，补成「B-8870 · Airbus A330-300」会喧宾夺主，
    /// 所以机型放到下面的摘要行里。
    /// </summary>
    private IReadOnlyList<string> _summaryColumns = Array.Empty<string>();

    /// <summary>
    /// 当前选中分组行的<b>展示标题</b>（如「香港国际机场 · HKG」）。
    /// 与 <see cref="SelectedGroup"/> 分开存：后者是查询用的原始分组键（"VHHH"），
    /// 拿展示标题去查库一定查不到。
    /// </summary>
    private string? _selectedGroupLabel;

    /// <summary>照片 → 自定义分组 Id 的映射（仅当前注册号范围）。</summary>
    private readonly Dictionary<long, long> _photoGroupMap = new();

    /// <summary>
    /// 当前选中的子分组是否为「未填写」兜底项。
    /// 该行没有真实的值可匹配，只能改用 <c>child IS NULL OR TRIM(child)=''</c> 查询。
    /// </summary>
    private bool _selectedSubGroupIsMissing;

    /// <summary>当前选中的外层分组是否为「未填写」兜底项（如「未填写注册号」）。</summary>
    private bool _selectedGroupIsMissing;

    /// <summary>外层分组列的匹配方式。</summary>
    private ColumnValueMatch GroupMatch => _selectedGroupIsMissing
        ? ColumnValueMatch.Missing
        : ColumnValueMatch.Of(SelectedGroup ?? string.Empty);

    /// <summary>子分组（注册号）列的匹配方式。</summary>
    private ColumnValueMatch SubGroupMatch => _selectedSubGroupIsMissing
        ? ColumnValueMatch.Missing
        : ColumnValueMatch.Of(SelectedSubGroup ?? string.Empty);

    public GroupedPhotoViewModel(IDatabaseService db, string groupColumn, string dimensionLabel,
        bool enableDayGrouping = false, bool enableRegistrationSubLevel = false,
        bool enableUserGroups = false)
    {
        _db = db;
        _groupColumn = groupColumn;
        _dimensionLabel = dimensionLabel;
        _enableDayGrouping = enableDayGrouping;
        _enableRegistrationSubLevel = enableRegistrationSubLevel;
        _enableUserGroups = enableUserGroups;
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
    public void SwitchDimension(string groupColumn, string dimensionLabel, bool enableDayGrouping,
        bool enableRegistrationSubLevel, bool enableUserGroups,
        IReadOnlyList<string>? rowLabelColumns = null,
        IReadOnlyList<string>? summaryColumns = null)
    {
        var labels = rowLabelColumns ?? Array.Empty<string>();
        var summaries = summaryColumns ?? Array.Empty<string>();
        if (_groupColumn == groupColumn && _dimensionLabel == dimensionLabel
            && _enableDayGrouping == enableDayGrouping
            && _enableRegistrationSubLevel == enableRegistrationSubLevel
            && _enableUserGroups == enableUserGroups
            && _rowLabelColumns.SequenceEqual(labels)
            && _summaryColumns.SequenceEqual(summaries))
        {
            return;
        }

        _groupColumn = groupColumn;
        _dimensionLabel = dimensionLabel;
        _enableDayGrouping = enableDayGrouping;
        _enableRegistrationSubLevel = enableRegistrationSubLevel;
        _enableUserGroups = enableUserGroups;
        _rowLabelColumns = labels;
        _summaryColumns = summaries;

        // 清空上一维度的数据与状态，再刷新所有派生属性
        SelectedGroup = null;
        SelectedSubGroup = null;
        _selectedGroupLabel = null;
        _selectedGroupIsMissing = false;
        _selectedSubGroupIsMissing = false;
        Groups.Clear();
        SubGroups.Clear();
        Photos.Clear();
        Timeline.Clear();
        UserGroups.Clear();
        _photoGroupMap.Clear();
        RaiseStateProperties();
    }

    // ---------- 状态 ----------

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string StatusMessage { get; set; } = "准备就绪";

    /// <summary>分组列表（每个分组值一行）。</summary>
    public ObservableCollection<GroupItemViewModel> Groups { get; } = new();

    /// <summary>子分组列表（注册号）。仅在机型页（启用中间层）使用。</summary>
    public ObservableCollection<GroupItemViewModel> SubGroups { get; } = new();

    /// <summary>当前选中分组下的照片（平铺视图用；启用时间轴时不显示）。</summary>
    public ObservableCollection<PhotoCardViewModel> Photos { get; } = new();

    /// <summary>当前选中的分组值；null 表示未选中（显示分组列表）。</summary>
    [ObservableProperty] public partial string? SelectedGroup { get; set; }

    /// <summary>当前选中的子分组（注册号）值；null 表示尚未进入叶子层。</summary>
    [ObservableProperty] public partial string? SelectedSubGroup { get; set; }

    // ---------- 三级状态机 ----------
    //   列表态   : SelectedGroup == null
    //   子分组态 : SelectedGroup != null  &&  启用中间层 && SelectedSubGroup == null
    //   照片态   : SelectedGroup != null  && (!启用中间层 || SelectedSubGroup != null)

    /// <summary>是否处于「查看某分组下的照片」（叶子态）。</summary>
    public bool IsViewingGroup =>
        !string.IsNullOrEmpty(SelectedGroup) && !IsViewingSubGroups;

    /// <summary>是否处于「查看子分组（注册号）列表」态。</summary>
    public bool IsViewingSubGroups =>
        _enableRegistrationSubLevel
        && !string.IsNullOrEmpty(SelectedGroup)
        && string.IsNullOrEmpty(SelectedSubGroup);

    /// <summary>是否处于分组列表态（最外层）。</summary>
    public bool IsViewingGroupList => !IsViewingGroup && !IsViewingSubGroups;

    public string DimensionLabel => _dimensionLabel;

    /// <summary>分组依据的数据库列名（供页面判断是否需要重建 ViewModel）。</summary>
    public string GroupColumn => _groupColumn;

    /// <summary>
    /// 页面标题，随三级状态变化：
    /// 列表态「机型」→ 子分组态「机型 · A330-300」→ 照片态「机型 · A330-300 · B-8870」。
    ///
    /// <para>
    /// 带复合展示标签的维度（机场页＝「机场名 · IATA」）在照片态<b>只显示标签</b>：
    /// 标签本身已自带分隔符，再拼上维度名会变成
    /// 「机场 · 香港国际机场 · HKG」这种两个「·」的啰嗦写法。
    /// </para>
    /// </summary>
    public string HeaderText => IsViewingSubGroups
        ? $"{_dimensionLabel} · {GroupLabel}"
        : IsViewingGroup
            ? (HasCompositeLabel
                ? GroupLabel
                : _enableRegistrationSubLevel
                    ? $"{GroupLabel} · {SelectedSubGroup}"
                    : $"{_dimensionLabel} · {GroupLabel}")
            : _dimensionLabel;

    /// <summary>分组行是否带复合展示标签（机场页 = [机场名, IATA]）。</summary>
    private bool HasCompositeLabel => _rowLabelColumns.Count > 0;

    /// <summary>外层分组的展示标题；未选中时退回空串。</summary>
    private string GroupLabel => _selectedGroupLabel ?? SelectedGroup ?? string.Empty;

    // ---------- 可见性（XAML 直接绑定，避免转换器） ----------

    /// <summary>
    /// 统一重算所有「派生只读属性」的绑定。
    /// <para>
    /// 这些属性没有自己的 backing field，x:Bind 的 OneWay 无法自动感知变化，
    /// 必须在依赖状态（Groups / IsBusy / SelectedGroup / Timeline…）变化后显式通知。
    /// 早期版本漏掉对 <see cref="EmptyHintVisibility"/> 的通知，导致有数据时
    /// 仍然显示「暂无XX数据」——这里集中处理，避免再次遗漏。
    /// </para>
    /// </summary>
    private void RaiseStateProperties()
    {
        OnPropertyChanged(nameof(IsViewingGroup));
        OnPropertyChanged(nameof(IsViewingSubGroups));
        OnPropertyChanged(nameof(IsViewingGroupList));
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(GroupListVisibility));
        OnPropertyChanged(nameof(SubGroupListVisibility));
        OnPropertyChanged(nameof(DetailVisibility));
        OnPropertyChanged(nameof(BackButtonVisibility));
        OnPropertyChanged(nameof(GroupListSortVisibility));
        OnPropertyChanged(nameof(PhotoSortVisibility));
        OnPropertyChanged(nameof(TimelineVisibility));
        OnPropertyChanged(nameof(FlatPhotosVisibility));
        OnPropertyChanged(nameof(EmptyHintVisibility));
        OnPropertyChanged(nameof(SubGroupEmptyHintVisibility));
        OnPropertyChanged(nameof(HasTimeline));
        OnPropertyChanged(nameof(GroupingHintVisibility));
        OnPropertyChanged(nameof(UserGroupHint));
    }

    /// <summary>最外层分组列表（仅在列表态显示）。</summary>
    public Microsoft.UI.Xaml.Visibility GroupListVisibility =>
        IsViewingGroupList ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>中间层子分组列表（机型页的注册号列表）。</summary>
    public Microsoft.UI.Xaml.Visibility SubGroupListVisibility =>
        IsViewingSubGroups ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>「分组排序」下拉：最外层列表与中间层子分组列表共用，两态都显示。</summary>
    public Microsoft.UI.Xaml.Visibility GroupListSortVisibility =>
        (IsViewingGroupList || IsViewingSubGroups)
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>叶子态显示「照片排序」下拉。</summary>
    public Microsoft.UI.Xaml.Visibility PhotoSortVisibility =>
        IsViewingGroup ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility DetailVisibility =>
        IsViewingGroup ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>返回按钮：照片态与中间层都要显示，最外层列表态不显示。</summary>
    public Microsoft.UI.Xaml.Visibility BackButtonVisibility =>
        (IsViewingGroup || IsViewingSubGroups)
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>时间轴区域可见性（仅启用二层分组且有节点时）。</summary>
    public Microsoft.UI.Xaml.Visibility TimelineVisibility =>
        HasTimeline ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility FlatPhotosVisibility =>
        (!EnableDayGrouping || !HasTimeline)
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>空状态只在最外层列表态有意义（中间/叶子层为空由各自的面板给提示）。</summary>
    public Microsoft.UI.Xaml.Visibility EmptyHintVisibility =>
        (IsViewingGroupList && Groups.Count == 0 && !IsBusy)
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>分组用法提示条：仅在「注册号」维度且已进入照片态时显示（自定义分组只属于注册号页）。</summary>
    public Microsoft.UI.Xaml.Visibility GroupingHintVisibility =>
        (_enableUserGroups && IsViewingGroup)
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>子分组为空的提示（机型页：该机型下的照片都没填注册号）。</summary>
    public Microsoft.UI.Xaml.Visibility SubGroupEmptyHintVisibility =>
        (IsViewingSubGroups && SubGroups.Count == 0 && !IsBusy)
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>空状态提示文案。</summary>
    public string EmptyHint => $"暂无{_dimensionLabel}数据。请先导入照片并填写{_dimensionLabel}信息。";

    /// <summary>注册号详情里的分组用法提示。</summary>
    public string UserGroupHint =>
        UserGroups.Count == 0
            ? "右键任意照片 →「加入分组…」，即可把这架飞机的照片归入自定义分组（如「2024 珠海航展」）。未分组的照片按天自动成组。"
            : $"已有 {UserGroups.Count} 个自定义分组：{string.Join("、", UserGroups.Select(g => g.Name))}。右键照片可调整归属。";

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

    /// <summary>是否启用时间轴（按天自动分组）。</summary>
    public bool EnableDayGrouping => _enableDayGrouping;

    /// <summary>是否启用「注册号」中间层（机型页为 true）。</summary>
    public bool EnableRegistrationSubLevel => _enableRegistrationSubLevel;

    /// <summary>是否允许用户自定义分组（仅注册号页为 true）。页面据此决定右键菜单是否给出分组项。</summary>
    public bool EnableUserGroups => _enableUserGroups;

    partial void OnSortIndexChanged(int value)
    {
        if (IsViewingGroup) _ = LoadGroupPhotosAsync();
    }

    partial void OnGroupSortIndexChanged(int value)
    {
        // 列表态下切换排序 → 原地重排已加载的分组，无需重新查询数据库。
        // 外层分组与中间层子分组共用同一个排序下拉，两个列表都要重排。
        ApplyGroupSort();
        ApplySubGroupSort();
    }

    /// <summary>按当前 GroupSortIndex 对 <see cref="Groups"/> 原地重排。</summary>
    private void ApplyGroupSort() => ApplySortTo(Groups);

    /// <summary>按当前 GroupSortIndex 对 <see cref="SubGroups"/> 原地重排。</summary>
    private void ApplySubGroupSort() => ApplySortTo(SubGroups);

    /// <summary>
    /// 对任一「分组行」列表原地重排。外层分组与中间层子分组共用同一套排序规则，
    /// 抽出来避免两处各写一遍导致排序行为不一致。
    /// </summary>
    private void ApplySortTo(ObservableCollection<GroupItemViewModel> list)
    {
        if (list.Count <= 1) return;

        IEnumerable<GroupItemViewModel> ordered = GroupSortIndex switch
        {
            1 => list.OrderBy(g => g.Count).ThenBy(g => g.DisplayText, StringComparer.OrdinalIgnoreCase),
            2 => list.OrderBy(g => g.DisplayText, StringComparer.OrdinalIgnoreCase),
            3 => list.OrderByDescending(g => g.FirstShotAt ?? DateTimeOffset.MinValue)
                       .ThenBy(g => g.DisplayText, StringComparer.OrdinalIgnoreCase),
            4 => list.OrderBy(g => g.FirstShotAt ?? DateTimeOffset.MaxValue)
                       .ThenBy(g => g.DisplayText, StringComparer.OrdinalIgnoreCase),
            _ => list.OrderByDescending(g => g.Count).ThenBy(g => g.DisplayText, StringComparer.OrdinalIgnoreCase),
        };

        var sorted = ordered.ToList();
        // ObservableCollection 没有 Sort()，用 Move 原地重排以保留选中状态
        for (var target = 0; target < sorted.Count; target++)
        {
            var current = list.IndexOf(sorted[target]);
            if (current != target) list.Move(current, target);
        }
    }

    partial void OnSelectedGroupChanged(string? value) => RaiseStateProperties();

    partial void OnSelectedSubGroupChanged(string? value) => RaiseStateProperties();

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
            var counts = await _db.GetGroupCountsAsync(
                _groupColumn, rowLabelColumns: _rowLabelColumns, summaryColumns: _summaryColumns);
            Groups.Clear();
            foreach (var c in counts.Values)
            {
                Groups.Add(new GroupItemViewModel(c, _dimensionLabel));
            }
            // 未填写该维度的照片也必须有一行入口 —— 否则它们在对应页面彻底看不到
            //（GetGroupCountsAsync 的值分布会把该列为空的记录整个过滤掉）
            if (counts.Missing is not null)
            {
                Groups.Add(new GroupItemViewModel(
                    counts.Missing, _dimensionLabel,
                    isMissing: true, displayOverride: $"未填写{_dimensionLabel}"));
            }
            ApplyGroupSort();

            var total = counts.Values.Sum(c => c.Count) + (counts.Missing?.Count ?? 0);
            StatusMessage = total == 0
                ? $"暂无{_dimensionLabel}数据（先导入照片并填写{_dimensionLabel}）"
                : counts.Missing is { Count: > 0 } miss
                    ? $"共 {counts.Values.Count} 个{_dimensionLabel}，另有 {miss.Count} 张未填写{_dimensionLabel}"
                    : $"共 {counts.Values.Count} 个{_dimensionLabel}";
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

    /// <summary>
    /// 点击最外层分组。机型页（启用中间层）先进入「注册号」列表，
    /// 其余维度直接进照片态。
    /// </summary>
    [RelayCommand]
    public async Task OpenGroupAsync(GroupItemViewModel? group)
    {
        if (group is null) return;
        SelectedGroup = group.Value;              // 查询用：原始分组键
        _selectedGroupLabel = group.DisplayText;  // 展示用：可能含附加字段
        SelectedSubGroup = null;
        _selectedGroupIsMissing = group.IsMissing;
        _selectedSubGroupIsMissing = false;

        if (_enableRegistrationSubLevel)
        {
            await LoadSubGroupsAsync();
        }
        else
        {
            await LoadGroupPhotosAsync();
        }
    }

    /// <summary>
    /// 点击中间层的注册号 → 进入照片态。
    /// 兜底行（未填写注册号）走单独查询分支。
    /// </summary>
    [RelayCommand]
    public async Task OpenSubGroupAsync(GroupItemViewModel? subGroup)
    {
        if (subGroup is null) return;
        SelectedSubGroup = subGroup.Value;
        _selectedSubGroupIsMissing = subGroup.IsMissing;
        await LoadGroupPhotosAsync();
    }

    /// <summary>
    /// 返回上一级：照片态 →（有中间层则回中间层）→ 列表态。
    /// 机型页因此是逐级回退，不会一步跳回最外层。
    /// </summary>
    [RelayCommand]
    public async Task GoBackAsync()
    {
        if (IsViewingGroup && _enableRegistrationSubLevel && !string.IsNullOrEmpty(SelectedSubGroup))
        {
            SelectedSubGroup = null;
            _selectedSubGroupIsMissing = false;
            ClearPhotoState();
            await LoadSubGroupsAsync();
            return;
        }

        SelectedSubGroup = null;
        SelectedGroup = null;
        _selectedGroupLabel = null;
        _selectedGroupIsMissing = false;
        _selectedSubGroupIsMissing = false;
        ClearPhotoState();
        RaiseStateProperties();
    }

    /// <summary>清空照片态相关集合（不改变选中值）。</summary>
    private void ClearPhotoState()
    {
        Photos.Clear();
        Timeline.Clear();
        UserGroups.Clear();
        _photoGroupMap.Clear();
    }

    /// <summary>重新加载当前叶子层（分组调整后调用）。</summary>
    public Task ReloadCurrentGroupAsync() => LoadGroupPhotosAsync();

    /// <summary>中间层：加载当前分组（机型）下各注册号的统计。</summary>
    private async Task LoadSubGroupsAsync()
    {
        if (string.IsNullOrEmpty(SelectedGroup)) return;
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var counts = await _db.GetSubGroupCountsAsync(_groupColumn, GroupMatch, "registration_number");

            SubGroups.Clear();
            foreach (var c in counts.Values)
            {
                SubGroups.Add(new GroupItemViewModel(c, "注册号"));
            }
            // 未填写注册号的照片必须也有入口 —— 否则它们在机型页彻底看不到
            //（值分布查询会把子列为空的记录整个过滤掉）
            if (counts.Missing is not null)
            {
                SubGroups.Add(new GroupItemViewModel(
                    counts.Missing, "注册号", isMissing: true, displayOverride: "未填写注册号"));
            }
            ApplySubGroupSort();

            var photoTotal = counts.Values.Sum(c => c.Count) + (counts.Missing?.Count ?? 0);
            StatusMessage = (counts.Values.Count, counts.Missing) switch
            {
                (0, null) => $"{SelectedGroup}：暂无照片",
                (0, { } m) => $"{SelectedGroup}：{m.Count} 张照片（均未填写注册号）",
                _ => counts.Missing is { Count: > 0 } miss
                    ? $"{SelectedGroup}：{counts.Values.Count} 架飞机，共 {photoTotal} 张照片，其中 {miss.Count} 张未填写注册号"
                    : $"{SelectedGroup}：{counts.Values.Count} 架飞机，共 {photoTotal} 张照片",
            };
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

            // 机型页已下钻到注册号 → 双列匹配（含两侧的「未填写」桶）；
            // 其余维度只按单一分组列
            var items = (_enableRegistrationSubLevel && !string.IsNullOrEmpty(SelectedSubGroup))
                ? await _db.GetPhotosByTwoColumnValuesAsync(
                    _groupColumn, GroupMatch, "registration_number", SubGroupMatch, order)
                : await _db.GetPhotosByColumnValueAsync(_groupColumn, GroupMatch, order);

            // 每张照片只建一个卡片实例，平铺区与时间轴共用，避免重复解码缩略图
            //
            // 只有「注册号」页把卡片文字挪到日期说明区（卡片只留图片）；
            // 机型 / 机场页保持原样，文字仍印在卡片上。
            var cards = items
                .Select(p => new PhotoCardViewModel(p, App.UiDispatcher)
                {
                    ShowCardText = !_enableUserGroups,
                })
                .ToList();

            Photos.Clear();
            foreach (var c in cards) Photos.Add(c);

            // 自定义分组只在注册号维度生效；其他维度传 null（只按天）
            await BuildTimelineAsync(items, cards, _enableUserGroups ? SelectedGroup : null);

            var scopeLabel = _enableRegistrationSubLevel && !string.IsNullOrEmpty(SelectedSubGroup)
                ? $"{GroupLabel} · {SelectedSubGroup}"
                : GroupLabel;

            var groupHint = _enableUserGroups && UserGroups.Count > 0
                ? $"，{UserGroups.Count} 个自定义分组"
                : string.Empty;
            var dayCount = Timeline.Count(n => !n.IsUserGroup);
            var timelineHint = EnableDayGrouping && dayCount > 0 ? $"，按 {dayCount} 天分组" : string.Empty;
            StatusMessage = $"{scopeLabel}：{items.Count} 张照片{groupHint}{timelineHint}";
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

    // ---------- 时间轴（二层分组：自定义分组 + 自动按天） ----------

    /// <summary>时间轴节点集合。固定实例，切换维度时只清空内容。</summary>
    public ObservableCollection<TimelineNode> Timeline { get; } = new();

    /// <summary>当前注册号下的自定义分组。</summary>
    public ObservableCollection<PhotoGroup> UserGroups { get; } = new();

    public bool HasTimeline => EnableDayGrouping && Timeline.Count > 0;

    /// <summary>
    /// 把一组照片的元信息拼成日期节点说明区要显示的文本，<b>一行一张</b>。
    ///
    /// <para>
    /// 只在<b>「注册号」页</b>使用：该页把卡片上的文字挪到了这里，
    /// 而一个节点本来就是同一架飞机，所以<b>不重复印注册号</b>。
    /// 机型 / 机场页的卡片自带文字，说明区不显示这些内容。
    /// </para>
    /// <para>
    /// <b>不含拍摄日期时间</b>：节点标题已经是日期了，再逐行重复一遍纯属噪音。
    /// </para>
    /// </summary>
    private static string BuildPhotoInfoText(IReadOnlyList<PhotoCardViewModel> cards)
    {
        return string.Join("\n", cards.Select(c =>
        {
            var airport = string.IsNullOrWhiteSpace(c.AirportDisplay)
                ? string.Empty
                : " · " + c.AirportDisplay;
            return c.AircraftDisplay + airport;
        }));
    }

    /// <summary>
    /// 构建时间轴。
    ///
    /// <para>
    /// 分区规则（保证每张照片只出现一次）：
    /// 先按自定义分组归属切出「自定义分组节点」；剩余未归属的照片再按拍摄日期切出「按天节点」。
    /// 节点按代表时间排序，代表时间取节点内<b>最晚</b>的拍摄时间（自定义分组内若无任何带时间的照片，
    /// 退化为分组创建时间），这样新拍的会自动浮到最上面。
    /// </para>
    /// </summary>
    /// <param name="userGroupScope">
    /// 自定义分组的作用域（注册号）。<b>null 表示该维度不支持自定义分组</b>
    /// （机型页 / 机场页），此时全部照片都走「按天」节点。
    /// </param>
    private async Task BuildTimelineAsync(
        IReadOnlyList<Photo> photos,
        IReadOnlyList<PhotoCardViewModel> cards,
        string? userGroupScope)
    {
        Timeline.Clear();
        UserGroups.Clear();
        _photoGroupMap.Clear();

        if (!EnableDayGrouping)
        {
            RaiseStateProperties();
            return;
        }

        // 自定义分组只属于「注册号」维度：机型/机场页传 null，跳过整段查询，
        // 每张照片都落到按天节点。
        var groups = new List<PhotoGroup>();
        IReadOnlyDictionary<long, long> map = new Dictionary<long, long>();
        if (!string.IsNullOrEmpty(userGroupScope))
        {
            groups = (await _db.GetGroupsAsync(userGroupScope)).ToList();
            foreach (var g in groups) UserGroups.Add(g);

            map = await _db.GetPhotoGroupMapAsync(userGroupScope);
            foreach (var kv in map) _photoGroupMap[kv.Key] = kv.Value;
        }

        var cardById = cards.ToDictionary(c => c.Id);
        var nodes = new List<TimelineNode>();

        // userGroupScope 只有「注册号」页会传（机型 / 机场页不支持自定义分组），正好当判据：
        // 只有注册号页把卡片文字挪到日期说明区；机型 / 机场页卡片自带文字，说明区保持原样。
        var showPhotoInfo = !string.IsNullOrEmpty(userGroupScope);

        // 1) 自定义分组节点（空分组不进时间轴，避免占位噪音）
        foreach (var g in groups)
        {
            var members = photos
                .Where(p => map.TryGetValue(p.Id, out var gid) && gid == g.Id)
                .Select(p => cardById[p.Id])
                .ToList();
            if (members.Count == 0) continue;

            var sortKey = members
                .Where(c => c.Photo.ShotAt.HasValue)
                .Select(c => c.Photo.ShotAt!.Value.DateTime)
                .DefaultIfEmpty(g.CreatedAt.DateTime)
                .Max();

            nodes.Add(new TimelineNode(
                title: g.Name,
                subtitle: FormatRange(members),
                photoInfo: showPhotoInfo ? BuildPhotoInfoText(members) : string.Empty,
                sortKey: sortKey,
                isUserGroup: true,
                groupId: g.Id,
                photos: members));
        }

        // 2) 未归属任何自定义分组的照片 → 按拍摄日期成组
        var unassigned = photos
            .Where(p => !map.ContainsKey(p.Id))
            .Select(p => cardById[p.Id])
            .ToList();

        var byDay = unassigned
            .Where(c => c.Photo.ShotAt.HasValue)
            .GroupBy(c => c.Photo.ShotAt!.Value.Date)
            .OrderByDescending(g => g.Key);

        foreach (var day in byDay)
        {
            var members = day.ToList();
            nodes.Add(new TimelineNode(
                title: day.Key.ToString("yyyy-MM-dd"),
                subtitle: WeekdayText(day.Key) + " · 自动按天",
                photoInfo: showPhotoInfo ? BuildPhotoInfoText(members) : string.Empty,
                sortKey: day.Key,
                isUserGroup: false,
                groupId: null,
                photos: members));
        }

        // 3) 无拍摄时间的照片统一兜底成一个节点
        var noTime = unassigned.Where(c => !c.Photo.ShotAt.HasValue).ToList();
        if (noTime.Count > 0)
        {
            nodes.Add(new TimelineNode(
                title: "未知拍摄时间",
                subtitle: "自动按天",
                photoInfo: showPhotoInfo ? BuildPhotoInfoText(noTime) : string.Empty,
                sortKey: DateTime.MinValue,
                isUserGroup: false,
                groupId: null,
                photos: noTime));
        }

        // 排序方式跟随「照片排序」：只有"旧→新"时升序，其余一律新→旧
        var ascending = SortIndex == 1;
        var ordered = ascending
            ? nodes.OrderBy(n => n.SortKey)
            : nodes.OrderByDescending(n => n.SortKey);

        foreach (var n in ordered) Timeline.Add(n);
        RaiseStateProperties();
    }

    /// <summary>节点内拍摄时间范围摘要，例如「2024-05-01 09:12 ~ 2024-05-03 17:40」。</summary>
    private static string FormatRange(IReadOnlyList<PhotoCardViewModel> members)
    {
        var times = members
            .Where(c => c.Photo.ShotAt.HasValue)
            .Select(c => c.Photo.ShotAt!.Value)
            .OrderBy(t => t)
            .ToList();
        if (times.Count == 0) return "无拍摄时间";
        if (times.Count == 1) return times[0].ToString("yyyy-MM-dd HH:mm");

        var first = times[0];
        var last = times[^1];
        return first.Date == last.Date
            ? $"{first:yyyy-MM-dd HH:mm} ~ {last:HH:mm}"
            : $"{first:yyyy-MM-dd HH:mm} ~ {last:yyyy-MM-dd HH:mm}";
    }

    private static string WeekdayText(DateTime date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日",
    };

    // ---------- 自定义分组的读写 ----------

    /// <summary>查询某张照片当前归属的自定义分组 Id；未分组返回 null。</summary>
    public long? GroupIdOf(long photoId) =>
        _photoGroupMap.TryGetValue(photoId, out var gid) ? gid : null;

    /// <summary>新建（或复用同名）分组。</summary>
    public Task<PhotoGroup> CreateUserGroupAsync(string name) =>
        _db.CreateGroupAsync(SelectedGroup ?? string.Empty, name);

    /// <summary>把照片指派到分组；<paramref name="groupId"/> 为 null 表示移出分组。</summary>
    public async Task AssignToGroupAsync(long photoId, long? groupId)
    {
        await _db.AssignPhotoToGroupAsync(photoId, groupId);
        await LoadGroupPhotosAsync();
    }

    /// <summary>重命名分组。</summary>
    public async Task RenameUserGroupAsync(long groupId, string newName)
    {
        await _db.RenameGroupAsync(groupId, newName);
        await LoadGroupPhotosAsync();
    }

    /// <summary>删除分组（照片不会被删，只是回到按天节点）。</summary>
    public async Task DeleteUserGroupAsync(long groupId)
    {
        await _db.DeleteGroupAsync(groupId);
        await LoadGroupPhotosAsync();
    }
}

/// <summary>分组列表中的一行。</summary>
public sealed class GroupItemViewModel
{
    private readonly GroupCount _count;
    private readonly string? _displayOverride;

    public GroupItemViewModel(GroupCount count, string dimensionLabel)
        : this(count, dimensionLabel, isMissing: false, displayOverride: null)
    {
    }

    /// <param name="isMissing">
    /// 该行是否为「未填写」兜底项。<see cref="GetMissingSubGroupAsync"/> 返回的统计
    /// 没有真实子列值，只有数量与时间范围，因此需要单独标记，
    /// 点击时走 <c>child IS NULL OR TRIM(child)=''</c> 的查询分支。
    /// </param>
    /// <param name="displayOverride">该行显示用的标题（兜底项不能直接用空的 Value）。</param>
    public GroupItemViewModel(GroupCount count, string dimensionLabel, bool isMissing, string? displayOverride)
    {
        _count = count;
        DimensionLabel = dimensionLabel;
        IsMissing = isMissing;
        _displayOverride = displayOverride;
    }

    /// <summary>
    /// 分组键的<b>原始值</b>，用于查库（如 "VHHH"）。
    /// 界面上要显示的是 <see cref="DisplayText"/>，<b>不要</b>把两者混用 ——
    /// 拿展示标题去查库一定查不到。
    /// </summary>
    public string Value => _count.Value;

    /// <summary>
    /// 行<b>展示标题</b>。兜底项用 <c>_displayOverride</c>；
    /// 有附加展示字段（机场页的 [机场名, IATA]）时拼成「香港国际机场 · HKG」；
    /// 附加字段全为空则退回原始分组值，保证永远不出现空标题。
    /// </summary>
    public string DisplayText
    {
        get
        {
            if (_displayOverride is not null) return _displayOverride;
            if (_count.DisplayParts is not { Count: > 0 }) return _count.Value;

            var parts = _count.DisplayParts
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!.Trim())
                .ToList();
            return parts.Count == 0 ? _count.Value : string.Join(" · ", parts);
        }
    }

    /// <summary>是否为「未填写」兜底行。</summary>
    public bool IsMissing { get; }

    /// <summary>兜底徽标可见性。</summary>
    public Microsoft.UI.Xaml.Visibility MissingBadgeVisibility =>
        IsMissing ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public int Count => _count.Count;
    public string DimensionLabel { get; }

    /// <summary>该分组内最早的拍摄时间（用于列表页排序）。</summary>
    public DateTimeOffset? FirstShotAt => _count.FirstShotAt;

    /// <summary>该分组内最晚的拍摄时间。</summary>
    public DateTimeOffset? LastShotAt => _count.LastShotAt;

    /// <summary>照片张数，形如 "12 张"。</summary>
    public string CountText => $"{_count.Count} 张";

    /// <summary>
    /// 拍摄时间范围摘要，例如 "2024-03-01 ~ 2025-01-20"。
    /// <para>
    /// 注意：<see cref="Photo.ShotAt"/> 存的是相机本地时间（EXIF 无时区，按 UTC+0 原样保存，
    /// 见 ExifService），这里<b>不能</b>再 ToLocalTime，否则会整体偏移时区、日期串位。
    /// </para>
    /// </summary>
    public string RangeText =>
        _count.FirstShotAt.HasValue && _count.LastShotAt.HasValue
            ? $"{_count.FirstShotAt.Value:yyyy-MM-dd} ~ {_count.LastShotAt.Value:yyyy-MM-dd}"
            : "无拍摄时间";

    /// <summary>
    /// 分组行的小字摘要：<b>小字附件 + 拍摄时间范围</b>。
    ///
    /// <para>
    /// 注册号页的行标题只有注册号，看不出是哪种飞机，所以把机型补在这里，
    /// 例如「Airbus A330-300 · 2021-05-01 ~ 2026-10-02」。
    /// 附加字段由 <c>GetGroupCountsAsync</c> 的 <c>summaryColumns</c> 取回（每组取 MAX）。
    /// </para>
    ///
    /// <para>
    /// 机型页不补（行标题就是机型，重复）；机场页也不补（一个机场机型混杂，
    /// 取 MAX 会给出误导性的单一型号）。「一架飞机 = 一种机型」只有注册号维度成立。
    /// </para>
    ///
    /// <para>
    /// 「未填写」兜底行没有 <c>SummaryParts</c>，自动退回纯时间范围 ——
    /// 否则会把该桶里任意一张照片的机型当成整行的机型展示。
    /// </para>
    /// </summary>
    public string SummaryText
    {
        get
        {
            var parts = _count.SummaryParts?
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return parts is not { Count: > 0 }
                ? RangeText
                : $"{string.Join(" / ", parts)} · {RangeText}";
        }
    }
}

/// <summary>
/// 时间轴上的一个节点。要么是用户自定义分组，要么是按天自动分组，两者互斥。
/// </summary>
public sealed class TimelineNode
{
    public TimelineNode(string title, string subtitle, string photoInfo, DateTime sortKey,
        bool isUserGroup, long? groupId, IReadOnlyList<PhotoCardViewModel> photos)
    {
        Title = title;
        Subtitle = subtitle;
        PhotoInfoText = photoInfo;
        SortKey = sortKey;
        IsUserGroup = isUserGroup;
        GroupId = groupId;
        Photos = photos;
    }

    /// <summary>节点标题：自定义分组显示组名，按天显示日期。</summary>
    public string Title { get; }

    /// <summary>副标题：时间范围 / 星期 / 类型说明。</summary>
    public string Subtitle { get; }

    /// <summary>
    /// 节点内每张照片的元信息，<b>一行一张</b>，显示在日期下面的说明区。
    ///
    /// <para>
    /// 这些文字原先印在每张照片卡片下方，让卡片又高又吵。现在卡片只留图片，
    /// 信息统一挪到日期节点这里展示 —— 一眼就能看清这个节点里都是些什么飞机。
    /// </para>
    /// </summary>
    public string PhotoInfoText { get; }

    /// <summary>排序键（节点内最晚拍摄时间）。</summary>
    public DateTime SortKey { get; }

    /// <summary>true = 用户自定义分组；false = 按天自动分组。</summary>
    public bool IsUserGroup { get; }

    /// <summary>自定义分组 Id；按天节点为 null。</summary>
    public long? GroupId { get; }

    public IReadOnlyList<PhotoCardViewModel> Photos { get; }

    public string CountText => $"{Photos.Count} 张";

    /// <summary>自定义分组徽标可见性。</summary>
    public Microsoft.UI.Xaml.Visibility BadgeVisibility =>
        IsUserGroup ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>
    /// 按天节点圆点的可见性（与 <see cref="BadgeVisibility"/> 互斥）。
    /// <para>
    /// 这里刻意用「两个圆点 + 可见性切换」而不是在代码里查
    /// <c>Application.Current.Resources[...]</c> 取画刷：后者在资源键缺失时会抛异常，
    /// 而主题画刷交给 XAML 的 <c>{ThemeResource}</c> 解析既安全、也能自动跟随深浅色主题。
    /// </para>
    /// </summary>
    public Microsoft.UI.Xaml.Visibility DayDotVisibility =>
        IsUserGroup ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
}
