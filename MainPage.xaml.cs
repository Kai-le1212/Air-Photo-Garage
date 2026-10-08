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
    /// 用户从下拉候选中点选一项 → <b>直接落成 token</b>。
    /// <para>
    /// TokenizingTextBox 默认只在「回车 / 逗号」时才把文本提交为 token，
    /// 单纯点选建议项只会把文字填进输入框，看起来像「点了没反应」。
    /// 这里在 SuggestionChosen 阶段主动 AddTokenItem 提交，点一下即生效。
    /// </para>
    /// <para>
    /// 注意点：这两个事件的 sender 是 TokenizingTextBox <b>内部</b>的
    /// AutoSuggestBox（事件由其转发），并非 TokenizingTextBox 本身，
    /// 因此需要用 <see cref="FindOwningTokenizingTextBox"/> 沿祖先链找宿主控件。
    /// </para>
    /// </summary>
    private void OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        var ttb = FindOwningTokenizingTextBox(sender);
        var text = args.SelectedItem?.ToString();
        if (ttb is null || string.IsNullOrWhiteSpace(text)) return;

        // AddTokenItem(item, resetText: true) —— 加入 token 并清空输入框
        ttb.AddTokenItem(text, true);
        SyncTokenToViewModel(ttb);
        ViewModel.InvalidateFilter();
    }

    /// <summary>
    /// 输入过程中的实时同步：
    /// 直接把输入框里的文字写回筛选字段（用户不必回车也能生效），
    /// 同时保留 token 场景（点选建议项后 SelectedTokenText 优先）。
    /// </summary>
    private void OnTokenTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        var ttb = FindOwningTokenizingTextBox(sender);
        if (ttb is null) return;

        SyncTokenToViewModel(ttb);
        ViewModel.InvalidateFilter();
    }

    /// <summary>用户直接按回车提交输入框里的文字 → 同样落成 token。</summary>
    private void OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var ttb = FindOwningTokenizingTextBox(sender);
        if (ttb is null) return;

        var text = args.ChosenSuggestion?.ToString() ?? args.QueryText;
        if (string.IsNullOrWhiteSpace(text)) return;

        ttb.AddTokenItem(text.Trim(), true);
        SyncTokenToViewModel(ttb);
        ViewModel.InvalidateFilter();
    }

    /// <summary>
    /// 从一个元素沿视觉树向上找到最近的 <see cref="TokenizingTextBox"/>。
    /// SuggestionChosen / QuerySubmitted 的 sender 是内层 AutoSuggestBox，需要这样回溯宿主。
    /// </summary>
    private static TokenizingTextBox? FindOwningTokenizingTextBox(DependencyObject? node)
    {
        for (var cur = node; cur is not null; cur = VisualTreeHelper.GetParent(cur))
        {
            if (cur is TokenizingTextBox ttb) return ttb;
        }
        return null;
    }

    /// <summary>
    /// TokenizingTextBox 的 TokenItemAdded/Removed 事件统一路由到此。
    /// <para>
    /// 关键：token 的增删<b>不一定</b>同步更新控件的 Text 属性，因此在移除「叉」时
    /// ViewModel 的筛选字段可能仍是旧值，导致筛选无法取消。这里显式读取当前 token/文本
    /// 回写到 ViewModel，保证「叉」能真正清掉对应筛选条件。
    /// </para>
    /// </summary>
    private void OnTokenChanged(object sender, object e)
    {
        if (sender is TokenizingTextBox ttb)
        {
            SyncTokenToViewModel(ttb);
        }

        ViewModel.InvalidateFilter();
    }

    /// <summary>把控件当前 token / 待提交文本的第一个值回写到对应 ViewModel 筛选字段。</summary>
    private void SyncTokenToViewModel(TokenizingTextBox ttb)
    {
        var value = GetCurrentFilterValue(ttb);
        if (ReferenceEquals(ttb, TtbAircraft))
            ViewModel.AircraftModelFilter = value;
        else if (ReferenceEquals(ttb, TtbRegistration))
            ViewModel.RegistrationFilter = value;
        else if (ReferenceEquals(ttb, TtbAirport))
            ViewModel.AirportCodeFilter = value;
    }

    /// <summary>
    /// 取控件的当前筛选值。
    /// <para>
    /// 必须用 <see cref="TokenizingTextBox.SelectedTokenText"/> 与 <see cref="TokenizingTextBox.Text"/>：
    /// <see cref="TokenizingTextBox.ItemsSource"/> 是「已选 token 集合」，而候选列表挂在
    /// <c>SuggestedItemsSource</c> 上 —— 早期误把候选集合绑到 ItemsSource，导致取值逻辑拿到
    /// 的其实是候选而不是用户输入。这里改为按 token → 输入文本 → 首个已选 token 的顺序取值。
    /// </para>
    /// </summary>
    private static string? GetCurrentFilterValue(TokenizingTextBox ttb)
    {
        var selected = ttb.SelectedTokenText;
        if (!string.IsNullOrWhiteSpace(selected)) return selected.Trim();

        var text = ttb.Text;
        if (!string.IsNullOrWhiteSpace(text)) return text.Trim();

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
    /// 实现已抽到 <see cref="Views.PhotoEditDialog"/>，与三个分组页共用同一份逻辑。
    /// </summary>
    private async Task ShowEditDialogAsync(PhotoCardViewModel card)
    {
        var saved = await Views.PhotoEditDialog.ShowAsync(XamlRoot, card.Photo);
        if (saved)
        {
            // 立即刷新派生属性，避免 UI 还显示旧值
            card.RefreshDisplayProperties();
        }
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
