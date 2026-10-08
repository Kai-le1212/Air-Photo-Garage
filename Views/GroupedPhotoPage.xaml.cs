using System;
using System.Threading.Tasks;
using AirPhotoGarage.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
            _ = ShowPhotoDetailAsync(card);
        }
    }

    /// <summary>
    /// 详情弹窗。与照片墙共用 <see cref="PhotoDetailBuilder"/>，
    /// 因此同样展示全部字段 + 展开的原始 EXIF。
    /// </summary>
    private async Task ShowPhotoDetailAsync(PhotoCardViewModel card)
    {
        var content = await PhotoDetailBuilder.BuildContentAsync(card.Photo);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"照片详情 · #{card.Photo.Id}",
            Content = content,
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close,
        };
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
