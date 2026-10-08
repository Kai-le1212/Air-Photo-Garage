using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AirPhotoGarage.Models;
using AirPhotoGarage.ViewModels;
using CommunityToolkit.WinUI.Controls;
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
    /// <para>
    /// 关键：token 的增删<b>不一定</b>同步更新控件的 Text 属性，因此在移除「叉」时
    /// ViewModel 的筛选字段可能仍是旧值，导致筛选无法取消。这里显式读取当前 token
    /// 集合回写到 ViewModel，保证「叉」能真正清掉对应筛选条件。
    /// </para>
    /// </summary>
    private void OnTokenChanged(object sender, object e)
    {
        // token 事件触发时，控件的 token 集合已更新；Text 属性可能滞后。
        // 用 token 集合重建筛选值（取第一个非空 token；无 token 则视为清空）。
        if (sender is TokenizingTextBox ttb)
        {
            var value = GetFirstTokenText(ttb);
            if (ReferenceEquals(ttb, TtbAircraft))
                ViewModel.AircraftModelFilter = value;
            else if (ReferenceEquals(ttb, TtbRegistration))
                ViewModel.RegistrationFilter = value;
            else if (ReferenceEquals(ttb, TtbAirport))
                ViewModel.AirportCodeFilter = value;
        }

        ViewModel.InvalidateFilter();
    }

    /// <summary>
    /// 从 TokenizingTextBox 取出第一个有效 token 的文本；无 token 时返回 null。
    /// </summary>
    private static string? GetFirstTokenText(TokenizingTextBox ttb)
    {
        if (ttb.ItemsSource is not System.Collections.IEnumerable items) return null;
        foreach (var item in items)
        {
            var s = item?.ToString();
            if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
        }
        return null;
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
            _ = ShowDetailDialogSafeAsync(card);
        }
    }

    /// <summary>
    /// 卡片上的常驻「详细信息」按钮。
    /// 这是三条详情路径中最不依赖命中测试的一条，作为最终兜底。
    /// 按钮位于卡片 DataTemplate 内，其 DataContext 即 <see cref="PhotoCardViewModel"/>。
    /// </summary>
    private void OnCardDetailButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PhotoCardViewModel card })
        {
            _ = ShowDetailDialogSafeAsync(card);
        }
    }

    /// <summary>
    /// 包一层异常兜底：原先直接 `_ = ShowDetailDialogAsync(card)`，
    /// 一旦构建内容时抛异常，Task 被丢弃后异常完全静默（弹窗不出现且无任何提示）。
    /// 这里把失败原因写日志并在 UI 上给出提示，避免"点不开又不知道为什么"。
    /// </summary>
    private async Task ShowDetailDialogSafeAsync(PhotoCardViewModel card)
    {
        try
        {
            await ShowDetailDialogAsync(card);
        }
        catch (Exception ex)
        {
            App.LogError("ShowDetailDialog", ex);
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
    /// 右键卡片：弹出 MenuFlyout。RightTapped 与 ItemClick 走不同事件路径。
    /// </summary>
    private void OnCardRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not PhotoCardViewModel card) return;

        var menu = new MenuFlyout();

        var detailItem = new MenuFlyoutItem { Text = "查看详情" };
        detailItem.Icon = new FontIcon { Glyph = "\uE890" }; // Info
        // 注意：这里必须走 Safe 版本。原先直接用 ShowDetailDialogAsync，
        // 异常被丢弃的 Task 吞掉后表现为「点了没反应」。
        detailItem.Click += (_, _) => _ = ShowDetailDialogSafeAsync(card);
        menu.Items.Add(detailItem);

        var editItem = new MenuFlyoutItem { Text = "编辑信息..." };
        editItem.Icon = new FontIcon { Glyph = "\uE70F" }; // Edit
        editItem.Click += async (_, _) =>
        {
            try
            {
                await ShowEditDialogAsync(card);
            }
            catch (Exception ex)
            {
                App.Log($"ShowEditDialogAsync 异常: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        };
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
        var scroll = await Views.PhotoDetailBuilder.BuildContentAsync(photo);

        var dialog = Views.PhotoDetailBuilder.CreateDialog(
            XamlRoot, photo.Id, scroll, primaryButtonText: "编辑信息");

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            // 从详情页"编辑信息"按钮 → 立即跳到编辑对话框
            await ShowEditDialogAsync(card);
        }
    }


    // ---------- 编辑对话框 ----------

    /// <summary>
    /// 判断 <paramref name="node"/> 是否是 <paramref name="ancestor"/> 本身或其视觉树子孙。
    /// 用于 LostFocus 时判断焦点是否仍落在候选列表内部（ListView 内实际聚焦的是 ListViewItem）。
    /// </summary>
    private static bool IsDescendantOf(DependencyObject? node, DependencyObject ancestor)
    {
        for (var cur = node; cur is not null; cur = VisualTreeHelper.GetParent(cur))
        {
            if (ReferenceEquals(cur, ancestor)) return true;
        }
        return false;
    }

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

        var stack = new StackPanel { Spacing = 10, Width = 460 };  // 固定宽度，确保所有列等宽

        // ---------- 机型（TextBox + inline ListView 候选下拉） ----------
        // 不用 AutoSuggestBox（ControlTemplate 里有不可消除的内部死区导致输入框窄 ~25px）。
        // 不用 Popup（Popup 在 ContentDialog 内嵌会破坏 Modal 焦点链，导致 PrimaryButton 无法响应）。
        // 直接把 ListView 放在 TextBox 下方（同一个 StackPanel），Visibility 控制显隐。
        var tbModel = new TextBox
        {
            Header = "机型",
            Text = photo.AircraftModel ?? "",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var listModel = new ListView
        {
            MaxHeight = 180,
            MinHeight = 32,
            SelectionMode = ListViewSelectionMode.Single,
            Visibility = Visibility.Collapsed,  // 默认隐藏，输入时显示
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, -6, 0, 0),  // 紧贴 TextBox
        };

        IReadOnlyList<string> currentSuggestions = Array.Empty<string>();
        int selectedIndex = -1;

        void PickCurrent()
        {
            string pick;
            if (selectedIndex >= 0 && selectedIndex < currentSuggestions.Count)
                pick = currentSuggestions[selectedIndex];
            else if (currentSuggestions.Count > 0)
                pick = currentSuggestions[0];
            else
                return;
            tbModel.Text = pick;
            listModel.Visibility = Visibility.Collapsed;
        }

        void RefreshSuggestions()
        {
            var results = App.AircraftCatalog.Search(tbModel.Text ?? "").ToList();
            currentSuggestions = results;
            listModel.ItemsSource = results;
            if (results.Count > 0)
            {
                selectedIndex = 0;
                listModel.SelectedIndex = 0;
                listModel.Visibility = Visibility.Visible;
            }
            else
            {
                selectedIndex = -1;
                listModel.SelectedIndex = -1;
                listModel.Visibility = Visibility.Collapsed;
            }
        }

        tbModel.TextChanged += (s, e) => RefreshSuggestions();
        listModel.ItemClick += (s, e) =>
        {
            tbModel.Text = e.ClickedItem as string ?? tbModel.Text;
            listModel.Visibility = Visibility.Collapsed;
        };
        // 焦点离开机型 TextBox → 隐藏候选（但点候选时让 ListView 拿到焦点）
        tbModel.LostFocus += (s, e) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
                // 焦点可能在 ListView 内部的 ListViewItem 上，需沿视觉树向上判断是否为候选列表的子孙
                if (IsDescendantOf(focused, listModel)) return;  // 焦点在候选列表，保持显示
                listModel.Visibility = Visibility.Collapsed;
            });
        };

        tbModel.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((s, e) =>
        {
            if (currentSuggestions.Count == 0) return;
            switch (e.Key)
            {
                case VirtualKey.Down:
                    selectedIndex = Math.Min(selectedIndex + 1, currentSuggestions.Count - 1);
                    listModel.SelectedIndex = selectedIndex;
                    e.Handled = true;
                    break;
                case VirtualKey.Up:
                    selectedIndex = Math.Max(selectedIndex - 1, 0);
                    listModel.SelectedIndex = selectedIndex;
                    e.Handled = true;
                    break;
                case VirtualKey.Enter:
                    PickCurrent();
                    e.Handled = true;
                    break;
                case VirtualKey.Tab:
                    PickCurrent();
                    // 不 e.Handled = true，让 Tab 继续走焦点转移
                    break;
                case VirtualKey.Escape:
                    listModel.Visibility = Visibility.Collapsed;
                    e.Handled = true;
                    break;
            }
        }), handledEventsToo: true);

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

        // IATA 失焦/变化 → 查目录回填 ICAO + Name
        tbIata.TextChanged += (s, args) => FillAirportFromIata(tbIata, tbIcao, tbAirportName);
        // ICAO 失焦/变化 → 查目录回填 IATA + Name
        tbIcao.TextChanged += (s, args) => FillAirportFromIcao(tbIcao, tbIata, tbAirportName);
        // 名称变化 → 反查 IATA + ICAO
        tbAirportName.TextChanged += (s, args) => FillAirportFromName(tbAirportName, tbIata, tbIcao);

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

        stack.Children.Add(tbModel);
        stack.Children.Add(listModel);  // 候选列表（默认 Collapsed，输入时显示）
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
            MaxHeight = 560,  // 默认内容（约 460px）下不滚动；备注填长内容后撑高触发滚动
        };
        dialog.Content = scroll;
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        photo.AircraftModel = NullIfEmpty(tbModel.Text);
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

    /// <summary>
    /// 在可视化树里按名字查找子元素（深度优先）。
    /// 用于访问 WinUI 模板内部带 x:Name 的控件（如 AutoSuggestBox 内部的 QueryButton）。
    /// </summary>
    private static T? FindVisualChild<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T t && match(t)) return t;
            var deeper = FindVisualChild(child, match);
            if (deeper is not null) return deeper;
        }
        return null;
    }
}
