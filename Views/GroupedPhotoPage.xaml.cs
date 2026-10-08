using System;
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
        ViewModel = new GroupedPhotoViewModel(
            App.Database, GroupedPhotoPageParameter.Aircraft.Column,
            GroupedPhotoPageParameter.Aircraft.Label);
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var param = e.Parameter as GroupedPhotoPageParameter
                    ?? GroupedPhotoPageParameter.Aircraft;

        ViewModel.SwitchDimension(param.Column, param.Label, param.EnableDayGrouping);

        _ = ViewModel.LoadGroupsAsync();
    }

    /// <summary>点击分组列表中的某一项 → 进入该分组。</summary>
    private async void OnGroupClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not GroupItemViewModel item) return;
        await ViewModel.OpenGroupAsync(item);
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        ViewModel.BackToGroups();
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
    /// </summary>
    private void OnCardRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PhotoCardViewModel card })
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

        flyout.ShowAt(sender as FrameworkElement, new FlyoutShowOptions
        {
            Position = e.GetPosition(sender as UIElement),
        });
        e.Handled = true;
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

/// <summary>分组页导航参数。</summary>
public sealed class GroupedPhotoPageParameter
{
    public required string Column { get; init; }
    public required string Label { get; init; }
    public bool EnableDayGrouping { get; init; }

    public static GroupedPhotoPageParameter Aircraft => new()
    {
        Column = "aircraft_model",
        Label = "机型",
    };

    public static GroupedPhotoPageParameter Airport => new()
    {
        Column = "airport_icao",
        Label = "机场",
    };

    public static GroupedPhotoPageParameter Registration => new()
    {
        Column = "registration_number",
        Label = "注册号",
        EnableDayGrouping = true,
    };
}
