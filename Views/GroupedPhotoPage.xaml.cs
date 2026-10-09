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

        ViewModel.SwitchDimension(
            param.Column, param.Label,
            param.EnableDayGrouping, param.EnableRegistrationSubLevel, param.EnableUserGroups,
            param.RowLabelColumns);

        _ = ViewModel.LoadGroupsAsync();
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

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        // 逐级回退：照片态 →（机型页则回注册号中间层）→ 列表态
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
    /// 卡片上的常驻「详细信息」按钮。
    /// 这是三条详情路径中最不依赖命中测试的一条，作为最终兜底。
    /// 按钮在卡片模板内部，其 DataContext 即 <see cref="PhotoCardViewModel"/>。
    /// </summary>
    private void OnCardDetailButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PhotoCardViewModel card })
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

        var detailItem = new MenuFlyoutItem { Text = "查看详细信息" };
        detailItem.Click += (_, _) => _ = ShowPhotoDetailSafeAsync(card);
        flyout.Items.Add(detailItem);

        var editItem = new MenuFlyoutItem { Text = "编辑信息..." };
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
    /// 详情弹窗。与照片墙共用 <see cref="PhotoDetailBuilder"/>，
    /// 因此同样展示全部字段 + 展开的原始 EXIF。
    /// </summary>
    private async Task ShowPhotoDetailAsync(PhotoCardViewModel card)
    {
        var content = await PhotoDetailBuilder.BuildContentAsync(card.Photo);

        var dialog = PhotoDetailBuilder.CreateDialog(XamlRoot, card.Photo.Id, content);
        await dialog.ShowAsync();
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
    /// 机型页：机型 → 注册号 → 照片。
    /// 机型是共享概念，必须再落到具体哪一架飞机（注册号）才有管理意义。
    /// </summary>
    public static GroupedPhotoPageParameter Aircraft => new()
    {
        Column = "aircraft_model",
        Label = "机型",
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

    /// <summary>注册号页：注册号 → 该飞机的照片，按天分 + 支持自定义分组。</summary>
    public static GroupedPhotoPageParameter Registration => new()
    {
        Column = "registration_number",
        Label = "注册号",
        EnableDayGrouping = true,
        EnableUserGroups = true,
    };
}
