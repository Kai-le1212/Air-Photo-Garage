using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AirPhotoGarage.Models;
using AirPhotoGarage.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace AirPhotoGarage;

/// <summary>
/// 主页面：照片墙 + 筛选面板 + 导入向导覆盖层。
///
/// 卡片交互：
/// - 单击：弹出「详情」对话框（只读，显示大图与全部元数据）
/// - 右键：弹出 MenuFlyout（查看详情 / 编辑信息 / 删除）
/// </summary>
public sealed partial class MainPage : Page
{
    public GarageViewModel ViewModel { get; }

    public MainPage()
    {
        InitializeComponent();
        ViewModel = App.GarageViewModel;
        Loaded += OnLoadedAsync;
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedAsync;
        await ViewModel.InitializeAsync();
    }

    /// <summary>
    /// 触发导入流程：弹出文件选择器，把文件交给 <see cref="GarageViewModel.ImportPhotosCommand"/>，
    /// 由其内部调用 <see cref="ImportWizardViewModel.StartAsync"/> 让用户填写机型/注册号等信息。
    /// </summary>
    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.ViewMode = PickerViewMode.Thumbnail;
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".heic");
        picker.FileTypeFilter.Add(".webp");
        picker.FileTypeFilter.Add(".tif");
        picker.FileTypeFilter.Add(".tiff");
        picker.FileTypeFilter.Add(".dng");
        picker.FileTypeFilter.Add(".raw");

        var files = await picker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0) return;

        var paths = new List<string>(files.Count);
        foreach (var f in files)
        {
            paths.Add(f.Path);
        }
        await ViewModel.ImportPhotosCommand.ExecuteAsync(paths);
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.InvalidateFilter();
    }

    private void OnCalendarDateChanged(object sender, CalendarDatePickerDateChangedEventArgs e)
    {
        ViewModel.InvalidateFilter();
    }

    /// <summary>
    /// TokenizingTextBox 的 TokenItemAdded/Removed 事件统一路由到此。
    /// </summary>
    private void OnTokenChanged(object sender, object e)
    {
        ViewModel.InvalidateFilter();
    }

    private void OnApplyFilter(object sender, RoutedEventArgs e)
    {
        ViewModel.InvalidateFilter();
    }

    // ---------- 卡片交互：单击 / 右键 ----------

    /// <summary>
    /// GridView ItemClick（按 winui3-full-skill/snippets/collections/gridview.md）：
    /// 标准点击事件，比在 DataTemplate 内嵌套 Button + Click 更可靠。
    /// </summary>
    private void OnPhotoItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PhotoCardViewModel card)
        {
            _ = ShowDetailDialogAsync(card);
        }
    }

    /// <summary>
    /// 右键卡片：弹出 MenuFlyout。RightTapped 与 ItemClick 走不同事件路径。
    /// </summary>
    private void OnCardRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not PhotoCardViewModel card) return;

        var menu = new MenuFlyout();

        var detailItem = new MenuFlyoutItem { Text = "查看详情" };
        detailItem.Icon = new FontIcon { Glyph = "\uE890" }; // Info
        detailItem.Click += (_, _) => _ = ShowDetailDialogAsync(card);
        menu.Items.Add(detailItem);

        var editItem = new MenuFlyoutItem { Text = "编辑信息..." };
        editItem.Icon = new FontIcon { Glyph = "\uE70F" }; // Edit
        editItem.Click += (_, _) => _ = ShowEditDialogAsync(card);
        menu.Items.Add(editItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        var deleteItem = new MenuFlyoutItem { Text = "删除" };
        deleteItem.Icon = new FontIcon { Glyph = "\uE74D" }; // Delete
        deleteItem.Click += (_, _) => _ = ConfirmDeleteAsync(card);
        menu.Items.Add(deleteItem);

        var options = new FlyoutShowOptions
        {
            Position = e.GetPosition(fe),
            Placement = FlyoutPlacementMode.RightEdgeAlignedTop,
        };
        menu.ShowAt(fe, options);
        e.Handled = true;
    }

    // ---------- 详情对话框（只读） ----------

    /// <summary>
    /// 弹出详情对话框：
    /// - 左：大图
    /// - 右上：基本元数据（机型/注册号/机场/拍摄时间/备注）
    /// - 右下：EXIF 详细信息（相机/镜头/曝光参数/GPS）
    /// - 底部跨列：文件信息 / AI 识别 / 路径
    /// </summary>
    private async Task ShowDetailDialogAsync(PhotoCardViewModel card)
    {
        var photo = card.Photo;

        var img = new Image
        {
            Stretch = Stretch.UniformToFill,
            MaxWidth = 540,
            MaxHeight = 380,
        };
        try
        {
            var path = photo.ThumbnailPath ?? photo.FilePath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                var bmp = new BitmapImage();
                using var fs = File.OpenRead(path);
                await bmp.SetSourceAsync(fs.AsRandomAccessStream());
                img.Source = bmp;
            }
        }
        catch { /* ignore */ }

        // ---- 右上：基本元数据 ----
        var basicInfo = new StackPanel { Spacing = 4, MinWidth = 240 };
        AddRow(basicInfo, "机型", photo.AircraftModel);
        AddRow(basicInfo, "注册号", photo.RegistrationNumber);
        AddRow(basicInfo, "拍摄时间", photo.ShotAt?.ToString("yyyy-MM-dd HH:mm"));
        AddRow(basicInfo, "机场 IATA", photo.AirportIata);
        AddRow(basicInfo, "机场 ICAO", photo.AirportIcao);
        AddRow(basicInfo, "机场名称", photo.AirportName);
        AddRow(basicInfo, "机场代码 (旧)", photo.AirportCode);
        AddRow(basicInfo, "备注", photo.Notes);

        // ---- 右下：EXIF 详细信息 ----
        var exifInfo = new StackPanel { Spacing = 4, MinWidth = 240 };
        AddRow(exifInfo, "相机厂商", photo.CameraMake);
        AddRow(exifInfo, "相机型号", photo.CameraModel);
        AddRow(exifInfo, "镜头", photo.LensModel);
        AddRow(exifInfo, "焦距", photo.FocalLength.HasValue ? $"{photo.FocalLength.Value:0.##} mm" : null);
        AddRow(exifInfo, "光圈", photo.Aperture);
        AddRow(exifInfo, "快门", photo.ShutterSpeed);
        AddRow(exifInfo, "ISO", photo.Iso?.ToString());
        AddRow(exifInfo, "GPS 纬度", photo.Latitude?.ToString("0.######"));
        AddRow(exifInfo, "GPS 经度", photo.Longitude?.ToString("0.######"));

        // ---- 右上+右下并排 ----
        var rightGrid = new Grid();
        rightGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rightGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        rightGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(basicInfo, 0);
        Grid.SetColumn(exifInfo, 2);
        rightGrid.Children.Add(basicInfo);
        rightGrid.Children.Add(exifInfo);

        // ---- 底部：文件信息 ----
        var footerInfo = new StackPanel { Spacing = 4, Margin = new Thickness(0, 12, 0, 0) };
        AddRow(footerInfo, "文件大小", FormatFileSize(photo.FileSize));
        AddRow(footerInfo, "导入时间", photo.ImportedAt.ToString("yyyy-MM-dd HH:mm"));
        if (photo.RecognitionStatus > 0)
        {
            AddRow(footerInfo, "AI 识别", photo.RecognizedAircraftModel is null
                ? "已识别（无结果）"
                : $"{photo.RecognizedAircraftModel}（置信度 {(photo.RecognitionConfidence ?? 0):P0}）");
        }
        AddRow(footerInfo, "原图路径", photo.FilePath);
        AddRow(footerInfo, "缩略图路径", photo.ThumbnailPath);

        // ---- 整体 Grid：左大图 + 右上双列 + 底部跨列 ----
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(540) });          // 左：大图
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });           // 中：间距
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 右：双列
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });    // 行 0：内容
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });                     // 行 1：间距
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });    // 行 2：底部

        Grid.SetColumn(img, 0);
        Grid.SetRow(img, 0);
        body.Children.Add(img);

        Grid.SetColumn(rightGrid, 2);
        Grid.SetRow(rightGrid, 0);
        body.Children.Add(rightGrid);

        Grid.SetColumnSpan(footerInfo, 3);
        Grid.SetRow(footerInfo, 2);
        body.Children.Add(footerInfo);

        // 把 body 包在 ScrollViewer 里，让内容超出可视区域时可滚动
        var scroll = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 600,
        };

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = $"照片详情 · #{photo.Id}",
            PrimaryButtonText = "编辑信息",
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close,
            Content = scroll,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            // 从详情页"编辑信息"按钮 → 立即跳到编辑对话框
            await ShowEditDialogAsync(card);
        }
    }

    private static void AddRow(StackPanel parent, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var t1 = new TextBlock
        {
            Text = label,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Top,
        };
        var t2 = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(t1, 0);
        Grid.SetColumn(t2, 1);
        row.Children.Add(t1);
        row.Children.Add(t2);
        parent.Children.Add(row);
    }

    private static string? Combine(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a)) return b;
        if (string.IsNullOrWhiteSpace(b)) return a;
        return $"{a} {b}";
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0) return "-";
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int u = 0;
        while (size >= 1024 && u < units.Length - 1)
        {
            size /= 1024;
            u++;
        }
        return $"{size:0.##} {units[u]}";
    }

    // ---------- 编辑对话框 ----------

    /// <summary>
    /// 编辑对话框：修改已入库照片的元数据。
    /// - 机型用 AutoSuggestBox + 内置 AircraftCatalogService 提供候选
    /// - 机场三字段（IATA/ICAO/Name）任一变化时，其他两个自动回填
    /// </summary>
    private async Task ShowEditDialogAsync(PhotoCardViewModel card)
    {
        var photo = card.Photo;
        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = $"编辑照片 · #{photo.Id}",
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        var stack = new StackPanel { Spacing = 10, MinWidth = 460 };

        // ---------- 机型 AutoSuggestBox ----------
        var asbModel = new AutoSuggestBox
        {
            Header = "机型",
            Text = photo.AircraftModel ?? "",
            QueryIcon = new FontIcon { Glyph = "\uE721" }, // Search
            MaxHeight = 32,
        };
        asbModel.TextChanged += (s, args) =>
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                asbModel.ItemsSource = App.AircraftCatalog.Search(asbModel.Text);
            }
        };
        asbModel.SuggestionChosen += (s, args) =>
        {
            asbModel.Text = args.SelectedItem as string ?? asbModel.Text;
        };
        // 按 Enter / Tab 直接选中第一项候选（如果候选列表非空）。
        // 使用 handledEventsToo: true 是因为 AutoSuggestBox 内部可能已经处理了 KeyDown，
        // 默认订阅会被跳过。
        asbModel.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((s, e) =>
        {
            if ((e.Key == VirtualKey.Enter || e.Key == VirtualKey.Tab) &&
                asbModel.IsSuggestionListOpen &&
                asbModel.ItemsSource is IEnumerable<string> items &&
                items.Any())
            {
                asbModel.Text = items.First();
                asbModel.ItemsSource = null;
                asbModel.IsSuggestionListOpen = false;
                e.Handled = true;
            }
        }), handledEventsToo: true);

        var tbReg = new TextBox { Header = "注册号", Text = photo.RegistrationNumber ?? "" };

        // ---------- 机场三字段联动 ----------
        var tbIata = new TextBox
        {
            Header = "IATA 三字代码 (如 PEK)",
            Text = photo.AirportIata ?? "",
            MaxLength = 3,
        };
        var tbIcao = new TextBox
        {
            Header = "ICAO 四字代码 (如 ZBAA)",
            Text = photo.AirportIcao ?? "",
            MaxLength = 4,
        };
        var tbAirportName = new TextBox
        {
            Header = "机场名称",
            Text = photo.AirportName ?? "",
        };

        // IATA 失焦/变化 → 查目录回填 ICAO + Name
        tbIata.TextChanged += (s, args) => FillAirportFromIata(tbIata, tbIcao, tbAirportName);
        // ICAO 失焦/变化 → 查目录回填 IATA + Name
        tbIcao.TextChanged += (s, args) => FillAirportFromIcao(tbIcao, tbIata, tbAirportName);
        // 名称变化 → 反查 IATA + ICAO
        tbAirportName.TextChanged += (s, args) => FillAirportFromName(tbAirportName, tbIata, tbIcao);

        var tbNotes = new TextBox { Header = "备注", Text = photo.Notes ?? "", AcceptsReturn = true, Height = 80 };

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
            MaxHeight = 560,
        };
        dialog.Content = scroll;
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        photo.AircraftModel = NullIfEmpty(asbModel.Text);
        photo.RegistrationNumber = NullIfEmpty(tbReg.Text);
        photo.AirportIata = NullIfEmpty(tbIata.Text?.ToUpperInvariant());
        photo.AirportIcao = NullIfEmpty(tbIcao.Text?.ToUpperInvariant());
        photo.AirportName = NullIfEmpty(tbAirportName.Text);
        photo.AirportCode = photo.AirportIata ?? photo.AirportIcao; // 兼容旧字段
        photo.Notes = NullIfEmpty(tbNotes.Text);
        photo.RecognitionStatus = string.IsNullOrWhiteSpace(photo.AircraftModel) ? 0 : 2;

        await App.Database.UpdatePhotoAsync(photo);

        // 立即刷新派生属性，避免 UI 还显示旧值
        card.RefreshDisplayProperties();
    }

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

    // ---------- 删除确认 ----------

    /// <summary>
    /// 删除确认对话框。确认后调用 <see cref="GarageViewModel.DeletePhotoCommand"/>。
    /// 文件保留在磁盘，仅从数据库移除。
    /// </summary>
    private async Task ConfirmDeleteAsync(PhotoCardViewModel card)
    {
        var photo = card.Photo;
        var summary = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(photo.AircraftModel))
            summary.AppendLine($"机型：{photo.AircraftModel}");
        if (!string.IsNullOrWhiteSpace(photo.RegistrationNumber))
            summary.AppendLine($"注册号：{photo.RegistrationNumber}");
        if (photo.ShotAt.HasValue)
            summary.AppendLine($"拍摄时间：{photo.ShotAt:yyyy-MM-dd HH:mm}");
        if (summary.Length == 0) summary.AppendLine("(无元数据)");

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = $"确认删除照片 #{photo.Id}？",
            Content = summary.ToString() + "\n（仅从数据库移除，磁盘文件保留）",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        if (ViewModel.DeletePhotoCommand.CanExecute(card))
        {
            ViewModel.DeletePhotoCommand.Execute(card);
        }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
