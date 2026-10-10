using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AirPhotoGarage.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AirPhotoGarage.Views;

/// <summary>
/// 编辑照片信息对话框（拍摄时间 / 机型 / 注册号 / 机场三字段 / 相机与镜头参数 / 备注）的共用实现。
///
/// 抽出原因：照片墙（<see cref="MainPage"/>）与三个分组页（<see cref="GroupedPhotoPage"/>）
/// 都需要「编辑信息」入口，此前该逻辑只存在于 MainPage 的私有方法里，
/// 导致分组页右键菜单缺少编辑项。这里统一实现，两处共用，避免分叉。
/// </summary>
public static class PhotoEditDialog
{
    /// <summary>
    /// 弹出编辑对话框。用户点「保存」后写库并刷新卡片显示属性。
    /// </summary>
    /// <param name="xamlRoot">用于承载对话框的 XamlRoot。</param>
    /// <param name="photo">要编辑的照片实体（就地修改）。</param>
    /// <returns>用户是否保存了修改。</returns>
    /// <remarks>
    /// 修改 <see cref="Photo.ShotAt"/> 会影响「按天分组」的归属，
    /// 调用方应在保存后重新加载当前视图（见 <see cref="GroupedPhotoPage"/>）。
    /// </remarks>
    public static async Task<bool> ShowAsync(XamlRoot xamlRoot, Photo photo)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = $"编辑照片 · #{photo.Id}",
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        var stack = new StackPanel { Spacing = 10, Width = 460 };  // 固定宽度，确保所有列等宽

        // ---------- 机型（原生 AutoSuggestBox） ----------
        // 依 WinUI 3 规范使用 AutoSuggestBox：候选下拉是 Fluent 原生浮出层，
        // 自带「点击外部收起」「单击即选中」「键盘导航」等行为，
        // 无需自己用 ListView 模拟（自造下拉正是此前两个 bug 的根源）。
        var asbModel = new AutoSuggestBox
        {
            Header = "机型",
            PlaceholderText = "输入机型，或从下拉候选中选择",
            Text = photo.AircraftModel ?? "",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(asbModel, "机型");

        // 候选内容：目录查询（按输入做包含匹配）。
        IReadOnlyList<string> SearchModels(string? query)
        {
            if (string.IsNullOrWhiteSpace(query)) return Array.Empty<string>();
            return App.AircraftCatalog.Search(query).ToList();
        }

        // TextChanged：仅用户输入时才更新候选，避免程序化回填反复触发。
        asbModel.TextChanged += (s, e) =>
        {
            if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            asbModel.ItemsSource = SearchModels(asbModel.Text);
        };

        // 选中候选项：把文本定为选中值（原生行为会同时收起下拉）。
        asbModel.SuggestionChosen += (s, e) =>
        {
            if (e.SelectedItem is string picked) asbModel.Text = picked;
        };

        // 回车 / 点击查询按钮提交：把当前文本作为机型；若候选未选则用第一条。
        asbModel.QuerySubmitted += (s, e) =>
        {
            var chosen = e.ChosenSuggestion as string;
            if (!string.IsNullOrWhiteSpace(chosen)) asbModel.Text = chosen;
        };


        var tbReg = new TextBox
        {
            Header = "注册号",
            Text = photo.RegistrationNumber ?? "",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // ---------- 机场三字段联动 ----------
        var tbIata = new TextBox
        {
            Header = "IATA 三字代码 (如 PEK)",
            Text = photo.AirportIata ?? "",
            MaxLength = 3,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var tbIcao = new TextBox
        {
            Header = "ICAO 四字代码 (如 ZBAA)",
            Text = photo.AirportIcao ?? "",
            MaxLength = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var tbAirportName = new TextBox
        {
            Header = "机场名称",
            Text = photo.AirportName ?? "",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // IATA 变化 → 查目录回填 ICAO + Name
        tbIata.TextChanged += (s, args) => FillAirportFromIata(tbIata, tbIcao, tbAirportName);
        // ICAO 变化 → 查目录回填 IATA + Name
        tbIcao.TextChanged += (s, args) => FillAirportFromIcao(tbIcao, tbIata, tbAirportName);
        // 名称变化 → 反查 IATA + ICAO
        tbAirportName.TextChanged += (s, args) => FillAirportFromName(tbAirportName, tbIata, tbIcao);

        // ---------- 拍摄时间 ----------
        //
        // ⚠️ 时区约定（务必保持）：ShotAt 存的是「相机本地时间」，但按 TimeSpan.Zero 标记
        //（EXIF 规范不带时区，见 ExifService）。因此写回时必须用 TimeSpan.Zero 构造：
        //   ✔ new DateTimeOffset(墙上时间, TimeSpan.Zero)
        //   ✘ new DateTimeOffset(墙上时间, 本机偏移)  ← 会让整库时间平移 8 小时，
        //                                              连带按天分组整体错位
        // 读取时同理：只取 Year/Month/Day 这类「墙上时间分量」，不换算时区。
        var seedShot = photo.ShotAt ?? DateTimeOffset.Now;

        var dpShotDate = new DatePicker
        {
            Header = "拍摄日期",
            Date = new DateTimeOffset(seedShot.Year, seedShot.Month, seedShot.Day, 0, 0, 0, TimeSpan.Zero),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(dpShotDate, "拍摄日期");

        var tpShotTime = new TimePicker
        {
            Header = "拍摄时间",
            ClockIdentifier = "24HourClock",
            SelectedTime = new TimeSpan(seedShot.Hour, seedShot.Minute, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(tpShotTime, "拍摄时间");

        // 有些照片本来就没有拍摄时间（EXIF 缺失且未记录文件时间）。
        // 给一个显式开关，否则用户只能被迫填一个假时间。
        var cbNoShotTime = new CheckBox
        {
            Content = "无拍摄时间（保存为空）",
            IsChecked = !photo.ShotAt.HasValue,
        };
        cbNoShotTime.Checked += (_, _) => { dpShotDate.IsEnabled = false; tpShotTime.IsEnabled = false; };
        cbNoShotTime.Unchecked += (_, _) => { dpShotDate.IsEnabled = true; tpShotTime.IsEnabled = true; };
        dpShotDate.IsEnabled = cbNoShotTime.IsChecked != true;
        tpShotTime.IsEnabled = cbNoShotTime.IsChecked != true;

        var shotPanel = new StackPanel { Spacing = 10 };
        shotPanel.Children.Add(dpShotDate);
        shotPanel.Children.Add(tpShotTime);
        shotPanel.Children.Add(cbNoShotTime);

        // ---------- 相机与镜头参数（EXIF 自动读取，允许手动修正） ----------
        var tbCameraMake = new TextBox
        {
            Header = "相机厂牌",
            PlaceholderText = "如 NIKON CORPORATION",
            Text = photo.CameraMake ?? "",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextWrapping = TextWrapping.Wrap,   // 厂牌全称也可能很长，同样别截断
        };
        var tbCameraModel = new TextBox
        {
            Header = "相机型号",
            PlaceholderText = "如 NIKON Z6_2",
            Text = photo.CameraModel ?? "",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextWrapping = TextWrapping.Wrap,
        };
        var tbLensModel = new TextBox
        {
            Header = "镜头型号",
            PlaceholderText = "如 NIKKOR Z 100-400mm f/4.5-5.6 VR S",
            Text = photo.LensModel ?? "",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            // 镜头型号动辄二三十个字符（「NIKKOR Z 100-400mm f/4.5-5.6 VR S」），
            // 单行 TextBox 只会显示前半截、后半截看不到。改成换行显示。
            TextWrapping = TextWrapping.Wrap,
        };

        // NumberBox：空值用 NaN 表示，比 TextBox 手写解析更稳（不会因半截输入报错）
        var nbFocal = new NumberBox
        {
            Header = "焦距 (mm)",
            PlaceholderText = "留空表示未知",
            Value = photo.FocalLength ?? double.NaN,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var nbIso = new NumberBox
        {
            Header = "ISO",
            PlaceholderText = "留空表示未知",
            Value = photo.Iso ?? double.NaN,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var tbAperture = new TextBox
        {
            Header = "光圈",
            PlaceholderText = "如 f/5.6",
            Text = photo.Aperture ?? "",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var tbShutter = new TextBox
        {
            Header = "快门速度",
            PlaceholderText = "如 1/800s",
            Text = photo.ShutterSpeed ?? "",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var cameraPanel = new StackPanel { Spacing = 10 };
        cameraPanel.Children.Add(tbCameraMake);
        cameraPanel.Children.Add(tbCameraModel);
        cameraPanel.Children.Add(tbLensModel);
        cameraPanel.Children.Add(nbFocal);
        cameraPanel.Children.Add(tbAperture);
        cameraPanel.Children.Add(tbShutter);
        cameraPanel.Children.Add(nbIso);

        // 折叠起来：主诉是改拍摄时间，相机参数属偶发修正，不应把对话框撑得很长
        var expCamera = new Expander
        {
            Header = "相机与镜头参数",
            Content = cameraPanel,
            IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };

        var tbNotes = new TextBox
        {
            Header = "备注",
            Text = photo.Notes ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 80,    // 默认高度（空备注时）
            MaxHeight = 200,   // 超过后内部滚动（不被外层 ScrollViewer 顶出去）
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // 初始反向回填：让三个字段对齐（以 Name 为准反向查 IATA/ICAO）
        if (!string.IsNullOrWhiteSpace(photo.AirportName))
        {
            FillAirportFromName(tbAirportName, tbIata, tbIcao);
        }

        stack.Children.Add(asbModel);
        stack.Children.Add(tbReg);
        stack.Children.Add(tbIata);
        stack.Children.Add(tbIcao);
        stack.Children.Add(tbAirportName);
        stack.Children.Add(shotPanel);
        stack.Children.Add(expCamera);
        stack.Children.Add(tbNotes);

        if (photo.RecognizedAircraftModel is not null)
        {
            var hint = new TextBlock
            {
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Text = $"模型识别结果：{photo.RecognizedAircraftModel}（置信度 {(photo.RecognitionConfidence ?? 0):P0}）",
            };
            stack.Children.Insert(0, hint);
        }

        // 包一层 ScrollViewer，机场 / 备注字段多时可滚动
        var scroll = new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 620,  // 折叠相机参数时的默认高度不触发滚动；展开或备注写长后才滚动
        };
        dialog.Content = scroll;

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return false;

        photo.AircraftModel = NullIfEmpty(asbModel.Text);
        photo.RegistrationNumber = NullIfEmpty(tbReg.Text);
        photo.AirportIata = NullIfEmpty(tbIata.Text?.ToUpperInvariant());
        photo.AirportIcao = NullIfEmpty(tbIcao.Text?.ToUpperInvariant());
        photo.AirportName = NullIfEmpty(tbAirportName.Text);
        photo.AirportCode = photo.AirportIata ?? photo.AirportIcao; // 兼容旧字段

        // 拍摄时间：见上方时区约定 —— 一律以 TimeSpan.Zero 构造，不做时区换算
        if (cbNoShotTime.IsChecked == true)
        {
            photo.ShotAt = null;
        }
        else
        {
            var d = dpShotDate.Date;
            var t = tpShotTime.SelectedTime ?? TimeSpan.Zero;

            // 秒的处理：DatePicker + TimePicker 只到「分」粒度，秒会丢掉。
            // 若不特殊处理，用户只是打开对话框点了保存，EXIF 的秒数就会被静默清零
            //（实测 19:31:03 → 19:31:00）。规则：
            //   用户没动到分钟粒度 → 沿用原秒数（打开即保存不会丢数据）
            //   真的改了日期或时分 → 秒归零（用户改的就是这个时刻）
            var second = 0;
            if (photo.ShotAt is { } prev
                && prev.Year == d.Year && prev.Month == d.Month && prev.Day == d.Day
                && prev.Hour == t.Hours && prev.Minute == t.Minutes)
            {
                second = prev.Second;
            }

            var wallClock = new DateTime(
                d.Year, d.Month, d.Day, t.Hours, t.Minutes, second, DateTimeKind.Unspecified);
            photo.ShotAt = new DateTimeOffset(wallClock, TimeSpan.Zero);
        }

        // 相机与镜头参数：NumberBox 空值为 NaN，转成 null 落库
        photo.CameraMake = NullIfEmpty(tbCameraMake.Text);
        photo.CameraModel = NullIfEmpty(tbCameraModel.Text);
        photo.LensModel = NullIfEmpty(tbLensModel.Text);
        photo.FocalLength = double.IsNaN(nbFocal.Value) ? null : nbFocal.Value;
        photo.Aperture = NullIfEmpty(tbAperture.Text);
        photo.ShutterSpeed = NullIfEmpty(tbShutter.Text);
        photo.Iso = double.IsNaN(nbIso.Value) ? null : (int)Math.Round(nbIso.Value);

        photo.Notes = NullIfEmpty(tbNotes.Text);
        photo.RecognitionStatus = string.IsNullOrWhiteSpace(photo.AircraftModel) ? 0 : 2;

        await App.Database.UpdatePhotoAsync(photo);
        return true;
    }

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>用户输入 IATA 三字后，自动回填 ICAO + 机场名。</summary>
    private static void FillAirportFromIata(TextBox tbIata, TextBox tbIcao, TextBox tbName)
    {
        if (tbIata.Text.Length != 3) return;
        var airport = App.AirportCatalog.FindByIata(tbIata.Text);
        if (airport is null) return;
        if (!string.Equals(tbIcao.Text, airport.Icao, StringComparison.OrdinalIgnoreCase))
            tbIcao.Text = airport.Icao ?? "";
        if (!string.Equals(tbName.Text, airport.Name, StringComparison.Ordinal))
            tbName.Text = airport.Name;
    }

    /// <summary>用户输入 ICAO 四字后，自动回填 IATA + 机场名。</summary>
    private static void FillAirportFromIcao(TextBox tbIcao, TextBox tbIata, TextBox tbName)
    {
        if (tbIcao.Text.Length != 4) return;
        var airport = App.AirportCatalog.FindByIcao(tbIcao.Text);
        if (airport is null) return;
        if (!string.Equals(tbIata.Text, airport.Iata, StringComparison.OrdinalIgnoreCase))
            tbIata.Text = airport.Iata ?? "";
        if (!string.Equals(tbName.Text, airport.Name, StringComparison.Ordinal))
            tbName.Text = airport.Name;
    }

    /// <summary>用户输入机场名后，反查 IATA + ICAO。仅匹配唯一时回填。</summary>
    private static void FillAirportFromName(TextBox tbName, TextBox tbIata, TextBox tbIcao)
    {
        var results = App.AirportCatalog.SearchByName(tbName.Text);
        if (results.Count != 1) return;
        var airport = results[0];
        if (!string.Equals(tbIata.Text, airport.Iata, StringComparison.OrdinalIgnoreCase))
            tbIata.Text = airport.Iata ?? "";
        if (!string.Equals(tbIcao.Text, airport.Icao, StringComparison.OrdinalIgnoreCase))
            tbIcao.Text = airport.Icao ?? "";
    }
}
