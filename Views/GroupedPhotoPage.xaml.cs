using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AirPhotoGarage.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace AirPhotoGarage.Views;

/// <summary>
/// 通用分组浏览页。由「机型 / 机场 / 注册号」三个菜单共用，
/// 通过导航参数（<see cref="GroupedPhotoPageParameter"/>）决定分组维度。
/// </summary>
public sealed partial class GroupedPhotoPage : Page
{
    /// <summary>
    /// 必须在 InitializeComponent() 之前赋值，否则 x:Bind 求值时会空引用。
    /// 导航到本页时通过 <see cref="GroupedPhotoViewModel.SwitchDimension"/> 原地切换维度，
    /// 而不是替换实例——否则 x:Bind 订阅的仍是旧对象，派生属性通知会丢失。
    /// </summary>
    public GroupedPhotoViewModel ViewModel { get; }

    /// <summary>
    /// 本次导航是否由照片墙的注册号窗格<b>直接跳到某个分组</b>（参数带了 OpenGroupValue）。
    /// 决定「← 返回」是回照片墙，还是在本页内逐级回退。
    /// </summary>
    private bool _openedDirectlyToGroup;

    public GroupedPhotoPage()
    {
        // 用「机型」这一最完整的配置作初值；真实维度在 OnNavigatedTo 里切换。
        // ctor 与 OnNavigatedTo 传参必须一致，否则会多触发一次无谓的重置。
        var seed = GroupedPhotoPageParameter.Aircraft;
        ViewModel = new GroupedPhotoViewModel(
            App.Database, seed.Column, seed.Label,
            seed.EnableDayGrouping, seed.EnableRegistrationSubLevel, seed.EnableUserGroups);
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var param = e.Parameter as GroupedPhotoPageParameter
                    ?? GroupedPhotoPageParameter.Aircraft;

        // 记录来路：带 OpenGroupValue 说明是从照片墙的注册号窗格直接跳进来的，
        // 「← 返回」应当回照片墙而不是本页的分组列表。
        _openedDirectlyToGroup = !string.IsNullOrWhiteSpace(param.OpenGroupValue);

        ViewModel.SwitchDimension(
            param.Column, param.Label,
            param.EnableDayGrouping, param.EnableRegistrationSubLevel, param.EnableUserGroups,
            param.RowLabelColumns, param.SummaryColumns);

        _ = LoadAndMaybeOpenAsync(param.OpenGroupValue);
    }

    /// <summary>
    /// 先加载分组列表；若导航参数带了对目标分组（照片墙点窗格跳进来），
    /// 再自动打开它 —— 用户不必在列表里再找一遍。
    /// </summary>
    private async Task LoadAndMaybeOpenAsync(string? openGroupValue)
    {
        await ViewModel.LoadGroupsAsync();
        if (string.IsNullOrWhiteSpace(openGroupValue)) return;

        var target = ViewModel.Groups
            .FirstOrDefault(g => !g.IsMissing
                                 && string.Equals(g.Value, openGroupValue, StringComparison.OrdinalIgnoreCase));
        if (target is not null)
        {
            await ViewModel.OpenGroupAsync(target);
        }
    }

    /// <summary>点击中间层的注册号 → 进入该飞机（机型 + 注册号）的照片。</summary>
    private async void OnSubGroupClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not GroupItemViewModel item) return;
        await ViewModel.OpenSubGroupAsync(item);
    }

    /// <summary>点击分组列表中的某一项 → 进入该分组。</summary>
    private async void OnGroupClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not GroupItemViewModel item) return;
        await ViewModel.OpenGroupAsync(item);
    }

    /// <summary>
    /// 「← 返回」按钮。
    ///
    /// <para>
    /// 默认是**页内逐级回退**：照片态 →（机型页则回注册号中间层）→ 列表态。
    /// 但如果本页是<b>从照片墙的注册号窗格直接跳进来的</b>（参数带了目标分组），
    /// 且当前正停在那架飞机的详情上，那么返回应该**回到照片墙** ——
    /// 用户的来路是照片墙，把他丢到「注册号」列表会莫名其妙。
    /// </para>
    /// </summary>
    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (_openedDirectlyToGroup && ViewModel.IsViewingGroup && Frame.CanGoBack)
        {
            Frame.GoBack();
            return;
        }

        _ = ViewModel.GoBackAsync();
    }

    private void OnPhotoClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PhotoCardViewModel card)
        {
            _ = ShowPhotoDetailSafeAsync(card);
        }
    }

    /// <summary>
    /// 右键卡片 → 菜单。分组页此前只有左键 ItemClick 一条路径，
    /// 与照片墙的交互不对齐；这里补齐右键菜单，提供「查看详细信息」与「编辑信息」入口。
    ///
    /// <para>
    /// 注册号维度额外提供<b>二层分组</b>入口：加入分组 / 移出分组 / 重命名 / 解散。
    /// 之所以只在注册号页出现——只有它具备「同一注册号 = 同一架飞机」的语义，
    /// 机型与机场维度下跨飞机合并分组没有意义。
    /// </para>
    /// </summary>
    private void OnCardRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element
            || element.DataContext is not PhotoCardViewModel card)
        {
            return;
        }

        var flyout = new MenuFlyout();

        // ↓ 前两项的文案与图标与照片墙右键菜单<b>逐字一致</b>，不要再各写一套
        var detailItem = new MenuFlyoutItem { Text = "查看详情" };
        detailItem.Icon = new FontIcon { Glyph = "\uE890" }; // Info
        detailItem.Click += (_, _) => _ = ShowPhotoDetailSafeAsync(card);
        flyout.Items.Add(detailItem);

        var editItem = new MenuFlyoutItem { Text = "编辑信息..." };
        editItem.Icon = new FontIcon { Glyph = "\uE70F" }; // Edit
        editItem.Click += (_, _) => _ = ShowEditDialogSafeAsync(card);
        flyout.Items.Add(editItem);

        // 只有注册号维度才有「同一架飞机」的自定义分组语义
        if (ViewModel.EnableUserGroups)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());

            var groupItem = new MenuFlyoutItem { Text = "加入分组…" };
            groupItem.Icon = new FontIcon { Glyph = "\uE8B7" }; // Folder
            groupItem.Click += (_, _) => _ = ShowGroupPickerSafeAsync(card);
            flyout.Items.Add(groupItem);

            var currentGroupId = ViewModel.GroupIdOf(card.Id);
            if (currentGroupId.HasValue)
            {
                var currentName = ViewModel.UserGroups
                    .FirstOrDefault(g => g.Id == currentGroupId.Value)?.Name ?? "分组";

                var removeItem = new MenuFlyoutItem { Text = "移出分组" };
                removeItem.Icon = new FontIcon { Glyph = "\uE894" }; // Remove
                removeItem.Click += (_, _) => _ = RemoveFromGroupAsync(card);
                flyout.Items.Add(removeItem);

                var renameItem = new MenuFlyoutItem { Text = "重命名分组…" };
                renameItem.Icon = new FontIcon { Glyph = "\uE8AC" }; // Rename
                renameItem.Click += (_, _) => _ = RenameGroupSafeAsync(currentGroupId.Value, currentName);
                flyout.Items.Add(renameItem);

                var deleteItem = new MenuFlyoutItem { Text = "解散分组" };
                deleteItem.Icon = new FontIcon { Glyph = "\uE74D" }; // Delete
                deleteItem.Click += (_, _) => _ = DeleteGroupSafeAsync(currentGroupId.Value, currentName);
                flyout.Items.Add(deleteItem);
            }
        }

        // 删除放在最末并单独分隔 —— 破坏性操作不与常规操作混在一起。
        // 此前分组页<b>没有</b>「删除」，只有照片墙能删；现在两边一致。
        // 变量名加 Photo 前缀：上面分组块里已有一个 deleteItem（解散分组），避免重名。
        flyout.Items.Add(new MenuFlyoutSeparator());

        var deletePhotoItem = new MenuFlyoutItem { Text = "删除" };
        deletePhotoItem.Icon = new FontIcon { Glyph = "\uE74D" }; // Delete
        deletePhotoItem.Click += (_, _) => _ = ConfirmDeleteAsync(card);
        flyout.Items.Add(deletePhotoItem);

        flyout.ShowAt(element, new FlyoutShowOptions
        {
            Position = e.GetPosition(element),
        });
        e.Handled = true;
    }

    // ---------- 二层分组：加入 / 移出 / 重命名 / 解散 ----------

    /// <summary>「加入分组…」：选择既有分组，或输入名称新建，然后指派。</summary>
    private async Task ShowGroupPickerSafeAsync(PhotoCardViewModel card)
    {
        try
        {
            var result = await PhotoGroupDialog.PickAsync(
                XamlRoot,
                ViewModel.SelectedGroup ?? string.Empty,
                ViewModel.UserGroups.ToList(),
                ViewModel.GroupIdOf(card.Id));

            if (!result.Confirmed) return;

            if (result.Remove)
            {
                await ViewModel.AssignToGroupAsync(card.Id, null);
                return;
            }

            long? targetId = result.GroupId;
            if (!string.IsNullOrWhiteSpace(result.NewName))
            {
                // CreateGroupAsync 幂等：同名分组已存在时直接复用，不会重复建
                var created = await ViewModel.CreateUserGroupAsync(result.NewName!);
                targetId = created.Id;
            }
            if (targetId is null) return;

            await ViewModel.AssignToGroupAsync(card.Id, targetId);
        }
        catch (Exception ex)
        {
            await ShowGroupErrorAsync("加入分组失败", ex);
        }
    }

    /// <summary>把单张照片移出分组（照片回到「按天」节点，文件不受影响）。</summary>
    private async Task RemoveFromGroupAsync(PhotoCardViewModel card)
    {
        try
        {
            await ViewModel.AssignToGroupAsync(card.Id, null);
        }
        catch (Exception ex)
        {
            await ShowGroupErrorAsync("移出分组失败", ex);
        }
    }

    private async Task RenameGroupSafeAsync(long groupId, string currentName)
    {
        try
        {
            var newName = await PhotoGroupDialog.RenameAsync(XamlRoot, currentName);
            if (string.IsNullOrWhiteSpace(newName)) return;
            await ViewModel.RenameUserGroupAsync(groupId, newName!);
        }
        catch (Exception ex)
        {
            await ShowGroupErrorAsync("重命名分组失败", ex);
        }
    }

    private async Task DeleteGroupSafeAsync(long groupId, string groupName)
    {
        try
        {
            var count = ViewModel.Timeline.FirstOrDefault(n => n.GroupId == groupId)?.Photos.Count ?? 0;
            if (!await PhotoGroupDialog.ConfirmDeleteAsync(XamlRoot, groupName, count)) return;
            await ViewModel.DeleteUserGroupAsync(groupId);
        }
        catch (Exception ex)
        {
            await ShowGroupErrorAsync("解散分组失败", ex);
        }
    }

    /// <summary>
    /// 删除照片。确认框与落库走与照片墙共用的 <see cref="PhotoDeleteDialog"/>；
    /// 删除后重建当前视图 —— 时间轴节点、分组计数、空状态都要跟着变。
    /// </summary>
    private async Task ConfirmDeleteAsync(PhotoCardViewModel card)
    {
        try
        {
            if (!await PhotoDeleteDialog.ConfirmAndDeleteAsync(XamlRoot, card.Photo)) return;

            if (ViewModel.IsViewingGroup)
            {
                await ViewModel.ReloadCurrentGroupAsync();
            }
            else
            {
                await ViewModel.LoadGroupsAsync();
            }
        }
        catch (Exception ex)
        {
            await ShowGroupErrorAsync("删除照片失败", ex);
        }
    }

    /// <summary>分组操作失败的统一提示。分组涉及写库，异常必须显式暴露而不是静默吞掉。</summary>
    private async Task ShowGroupErrorAsync(string title, Exception ex)
    {
        App.LogError($"GroupedPhotoPage.{title}", ex);
        await new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock
            {
                Text = ex.Message,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            },
            CloseButtonText = "关闭",
        }.ShowAsync();
    }

    /// <summary>
    /// 编辑信息对话框（与照片墙共用 <see cref="PhotoEditDialog"/>）。
    /// 保存后刷新卡片显示属性；异常兜底避免静默失败。
    /// </summary>
    private async Task ShowEditDialogSafeAsync(PhotoCardViewModel card)
    {
        try
        {
            var saved = await PhotoEditDialog.ShowAsync(XamlRoot, card.Photo);
            if (saved)
            {
                card.RefreshDisplayProperties();

                // 拍摄时间决定这张照片落在时间轴的哪个「天」节点上。
                // 改完不重建时间轴的话，照片会留在旧的日期分组里，显示与数据对不上。
                if (ViewModel.EnableDayGrouping && ViewModel.IsViewingGroup)
                {
                    await ViewModel.ReloadCurrentGroupAsync();
                }
            }
        }
        catch (Exception ex)
        {
            App.LogError("GroupedPhotoPage.ShowEditDialog", ex);
            await new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "无法打开编辑对话框",
                Content = new TextBlock
                {
                    Text = ex.ToString(),
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                },
                CloseButtonText = "关闭",
            }.ShowAsync();
        }
    }

    /// <summary>
    /// 异常兜底：直接弃元 Task 会让构建详情时的异常完全静默，
    /// 表现为「点了没反应且无任何提示」。这里记录日志并给出可视反馈。
    /// </summary>
    private async Task ShowPhotoDetailSafeAsync(PhotoCardViewModel card)
    {
        try
        {
            await ShowPhotoDetailAsync(card);
        }
        catch (Exception ex)
        {
            App.LogError("GroupedPhotoPage.ShowPhotoDetail", ex);
            await new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "无法打开详细信息",
                Content = new TextBlock
                {
                    Text = ex.ToString(),
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                },
                CloseButtonText = "关闭",
            }.ShowAsync();
        }
    }

    /// <summary>
    /// 详情弹窗。与照片墙共用 <see cref="PhotoDetailBuilder"/>，因此同样展示全部字段
    /// 与展开的原始 EXIF；并且<b>同样带「编辑信息」主按钮</b> ——
    /// 此前分组页漏传了 <c>primaryButtonText</c>，导致详情里没有编辑入口，
    /// 只能靠右键绕。
    /// </summary>
    private async Task ShowPhotoDetailAsync(PhotoCardViewModel card)
    {
        var content = await PhotoDetailBuilder.BuildContentAsync(card.Photo);

        var dialog = PhotoDetailBuilder.CreateDialog(
            XamlRoot, card.Photo.Id, content, primaryButtonText: "编辑信息");

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            // 详情页点「编辑信息」→ 直接进编辑对话框（与照片墙行为一致）
            await ShowEditDialogSafeAsync(card);
        }
    }
}

/// <summary>分组页导航参数。三个菜单共用同一个 Page，靠本参数区分维度与层级行为。</summary>
public sealed class GroupedPhotoPageParameter
{
    public required string Column { get; init; }
    public required string Label { get; init; }

    /// <summary>是否按天分组（时间轴）。</summary>
    public bool EnableDayGrouping { get; init; }

    /// <summary>是否在分组详情里再插入一层「注册号」（机型页启用）。</summary>
    public bool EnableRegistrationSubLevel { get; init; }

    /// <summary>是否允许用户自定义分组（仅注册号页启用）。</summary>
    public bool EnableUserGroups { get; init; }

    /// <summary>
    /// 分组行额外展示的列。
    /// 机场页用它把「VHHH」这类裸码补成「香港国际机场 · HKG」：
    /// 分组键仍是 <see cref="Column"/>（ICAO），展示时才拼接附加字段。
    /// </summary>
    public IReadOnlyList<string> RowLabelColumns { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 分组行<b>小字摘要</b>里额外补的列。
    /// 注册号页用它把机型补进小字：「B-8870 / Airbus A330-300 · 起止日期」——
    /// 行标题只有注册号，看不出是哪种飞机。
    /// 与 <see cref="RowLabelColumns"/> 的区别是<b>进标题还是进小字</b>。
    /// </summary>
    public IReadOnlyList<string> SummaryColumns { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 进页后自动打开的分组值。
    /// 照片墙点注册号窗格跳过来时带上它，省得用户再在列表里找一遍。
    /// </summary>
    public string? OpenGroupValue { get; init; }

    /// <summary>
    /// 机型页：机型 → 注册号 → 照片（按天时间轴）。
    /// 机型是共享概念，必须再落到具体哪一架飞机（注册号）才有管理意义。
    /// 落到某架飞机后的照片同样按天成节点，与机场 / 注册号页保持一致
    /// （此前这里漏了 EnableDayGrouping，导致机型页的照片是平铺的、唯独它没有时间轴）。
    /// </summary>
    public static GroupedPhotoPageParameter Aircraft => new()
    {
        Column = "aircraft_model",
        Label = "机型",
        EnableDayGrouping = true,
        EnableRegistrationSubLevel = true,
    };

    /// <summary>机场页：机场 → 该机场的照片，按天分。行标题显示「机场名 · IATA」。</summary>
    public static GroupedPhotoPageParameter Airport => new()
    {
        Column = "airport_icao",
        Label = "机场",
        EnableDayGrouping = true,
        RowLabelColumns = new[] { "airport_name", "airport_iata" },
    };

    /// <summary>
    /// 注册号页：注册号 → 该飞机的照片，按天分 + 支持自定义分组。
    /// 小字里补机型：行标题只有注册号，看不出是哪种飞机，
    /// 而「一架飞机 = 一种机型」在注册号维度上是成立的（机型页/机场页不成立）。
    /// </summary>
    public static GroupedPhotoPageParameter Registration => new()
    {
        Column = "registration_number",
        Label = "注册号",
        EnableDayGrouping = true,
        EnableUserGroups = true,
        SummaryColumns = new[] { "aircraft_model" },
    };
}
