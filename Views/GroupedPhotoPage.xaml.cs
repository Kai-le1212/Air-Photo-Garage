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
    /// 导航到本页时（OnNavigatedTo）会按参数替换为对应维度的实例。
    /// </summary>
    public GroupedPhotoViewModel ViewModel { get; private set; }

    public GroupedPhotoPage()
    {
        // 默认机型维度，保证 InitializeComponent 阶段 x:Bind 有对象可绑
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

        // 参数已变 → 换一个 ViewModel 并刷新绑定；参数未变则复用当前实例
        if (ViewModel.DimensionLabel != param.Label || ViewModel.GroupColumn != param.Column)
        {
            ViewModel = new GroupedPhotoViewModel(App.Database, param.Column, param.Label)
            {
                EnableDayGrouping = param.EnableDayGrouping,
            };
            Bindings.Update();
        }

        _ = ViewModel.LoadGroupsAsync();
    }

    private async void OnGroupClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not GroupItemViewModel item) return;
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
    /// 详情弹窗。为避免与 MainPage 的实现重复，这里仅展示关键字段；
    /// 完整版（含 EXIF 展开）在照片墙页。后续可抽公共方法。
    /// </summary>
    private async Task ShowPhotoDetailAsync(PhotoCardViewModel card)
    {
        var photo = card.Photo;
        var panel = new StackPanel { Spacing = 6, MinWidth = 320 };

        void Row(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var t1 = new TextBlock
            {
                Text = label,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            };
            var t2 = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            Grid.SetColumn(t1, 0);
            Grid.SetColumn(t2, 1);
            g.Children.Add(t1);
            g.Children.Add(t2);
            panel.Children.Add(g);
        }

        Row("ID", photo.Id.ToString());
        Row("机型", photo.AircraftModel);
        Row("注册号", photo.RegistrationNumber);
        Row("拍摄时间", photo.ShotAt?.ToString("yyyy-MM-dd HH:mm:ss"));
        Row("机场 IATA", photo.AirportIata);
        Row("机场 ICAO", photo.AirportIcao);
        Row("机场名称", photo.AirportName);
        Row("备注", photo.Notes);
        Row("相机", photo.CameraModel);
        Row("镜头", photo.LensModel);
        Row("焦距", photo.FocalLength.HasValue ? $"{photo.FocalLength.Value:0.##} mm" : null);
        Row("光圈", photo.Aperture);
        Row("快门", photo.ShutterSpeed);
        Row("ISO", photo.Iso?.ToString());
        Row("原图路径", photo.FilePath);

        var scroll = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 520,
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"照片详情 · #{photo.Id}",
            Content = scroll,
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
