using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AirPhotoGarage.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace AirPhotoGarage.Views;

/// <summary>
/// 编辑照片信息对话框（机型 / 注册号 / 机场三字段 / 备注）的共用实现。
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

        // ---------- 机型（TextBox + inline ListView 候选下拉） ----------
        // 不用 AutoSuggestBox（ControlTemplate 里有不可消除的内部死区导致输入框窄 ~25px）。
        // 不用 Popup（Popup 在 ContentDialog 内嵌会破坏 Modal 焦点链，导致 PrimaryButton 无法响应）。
        // 直接把 ListView 放在 TextBox 下方（同一个 StackPanel），Visibility 控制显隐。
        //
        // 点击提交之所以不用 ListView.ItemClick：ItemClick 依赖 ListView 先拿到键盘焦点，
        // 在 ContentDialog 模态链下经常不触发（表现为「点了没反应」）。改为在
        // DataTemplate 的根元素上挂 Tapped —— 直接命中行本身，最可靠。
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
            SelectionMode = ListViewSelectionMode.None,   // 不参与选择，避免抢焦点
            IsItemClickEnabled = false,                   // 点击完全交给 DataTemplate 处理
            Visibility = Visibility.Collapsed,            // 默认隐藏，输入时显示
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, -6, 0, 0),          // 紧贴 TextBox
        };

        IReadOnlyList<string> currentSuggestions = Array.Empty<string>();
        int selectedIndex = -1;
        bool suppressModelRefresh = false;   // 程序化回填文本时，抑制候选刷新

        // 候选列表是否处于「用户正在浏览」状态：只有此时才让上下键/回车作用于候选，
        // 否则按键应原样交给 TextBox（避免候选列表把输入框的按键全部吃掉）。
        bool IsListVisible() => listModel.Visibility == Visibility.Visible;

        void HideSuggestions()
        {
            listModel.Visibility = Visibility.Collapsed;
            selectedIndex = -1;
        }

        /// <summary>
        /// 面板是否处于「用户正在浏览候选」状态。此标志为 false 时，
        /// TextChanged 一律不再弹出候选（用于点击提交后避免立刻又弹出来）。
        /// </summary>
        bool browsing = false;

        /// <summary>把候选填进 TextBox，收起列表，并把焦点交回 TextBox。</summary>
        void CommitSuggestion(string? pick)
        {
            if (string.IsNullOrWhiteSpace(pick)) return;

            browsing = false;                    // 提交后停止浏览，杜绝列表立刻重开
            suppressModelRefresh = true;
            try
            {
                tbModel.Text = pick;
                tbModel.SelectionStart = tbModel.Text.Length;
            }
            finally
            {
                suppressModelRefresh = false;
            }

            HideSuggestions();

            // 关键：把焦点交回 TextBox，否则后续回车/方向键会落到 ListView 上，
            // 表现为「点过一次候选之后，回车就再也选不动了」。
            tbModel.Focus(FocusState.Programmatic);
        }

        void RefreshSuggestions()
        {
            if (!browsing)
            {
                // 未处于浏览态（如刚从候选提交完）→ 不再弹列表。
                return;
            }

            var results = App.AircraftCatalog.Search(tbModel.Text ?? "").ToList();
            currentSuggestions = results;
            listModel.ItemsSource = results;

            if (results.Count > 0)
            {
                selectedIndex = 0;
                listModel.Visibility = Visibility.Visible;
            }
            else
            {
                HideSuggestions();
            }
        }

        // 候选项模板：根 Border 挂 Tapped —— 直接命中行，绕开 ItemClick 的焦点依赖。
        var suggestionTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
            "  <Border Padding='12,8' Background='Transparent'>" +
            "    <TextBlock Text='{Binding}' TextTrimming='CharacterEllipsis' />" +
            "  </Border>" +
            "</DataTemplate>");
        listModel.ItemTemplate = suggestionTemplate;

        // Tapped 用 AddHandler 且 handledEventsToo，确保即使内层 TextBlock 先处理了也能拿到。
        listModel.AddHandler(UIElement.TappedEvent, new TappedEventHandler((s, e) =>
        {
            // 从被点击的元素向上找到承载字符串的 DataContext
            if (e.OriginalSource is DependencyObject src)
            {
                var picked = FindRowDataContext(src, listModel);
                if (picked is not null)
                {
                    CommitSuggestion(picked);
                    e.Handled = true;
                }
            }
        }), handledEventsToo: true);

        tbModel.TextChanged += (s, e) =>
        {
            // 程序化回填（CommitSuggestion 内部赋值）时不要重新弹列表，
            // 否则收起后立刻又被打开，且会再次抢焦点。
            if (suppressModelRefresh) return;

            // 只有用户真实输入才进入「浏览候选」状态并弹列表。
            browsing = true;
            RefreshSuggestions();
        };

        // 焦点离开且新焦点不在候选列表内 → 收起候选。
        tbModel.LostFocus += (s, e) =>
        {
            tbModel.DispatcherQueue.TryEnqueue(() =>
            {
                if (!IsListVisible()) return;  // 已收起
                var focused = FocusManager.GetFocusedElement(xamlRoot) as DependencyObject;
                if (IsDescendantOf(focused, listModel)) return;
                HideSuggestions();
            });
        };

        // 键盘交互同时挂在 TextBox 与 ListView 上：
        // 这样即使焦点短暂落到 ListView，上下键/回车依然可控，
        // 从根本上避免「点过候选后回车失效」。
        void HandleSuggestionKey(KeyRoutedEventArgs e)
        {
            if (!IsListVisible() || currentSuggestions.Count == 0)
            {
                return;  // 候选未展开时完全不干预，按键照常进 TextBox
            }

            switch (e.Key)
            {
                case VirtualKey.Down:
                    selectedIndex = Math.Min(selectedIndex + 1, currentSuggestions.Count - 1);
                    e.Handled = true;
                    break;
                case VirtualKey.Up:
                    selectedIndex = Math.Max(selectedIndex - 1, 0);
                    e.Handled = true;
                    break;
                case VirtualKey.Enter:
                    CommitSuggestion(
                        selectedIndex >= 0 && selectedIndex < currentSuggestions.Count
                            ? currentSuggestions[selectedIndex]
                            : currentSuggestions[0]);
                    e.Handled = true;
                    break;
                case VirtualKey.Escape:
                    HideSuggestions();
                    e.Handled = true;
                    break;
            }
        }

        tbModel.KeyDown += (s, e) => HandleSuggestionKey(e);
        listModel.KeyDown += (s, e) => HandleSuggestionKey(e);

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
        if (result != ContentDialogResult.Primary) return false;

        photo.AircraftModel = NullIfEmpty(tbModel.Text);
        photo.RegistrationNumber = NullIfEmpty(tbReg.Text);
        photo.AirportIata = NullIfEmpty(tbIata.Text?.ToUpperInvariant());
        photo.AirportIcao = NullIfEmpty(tbIcao.Text?.ToUpperInvariant());
        photo.AirportName = NullIfEmpty(tbAirportName.Text);
        photo.AirportCode = photo.AirportIata ?? photo.AirportIcao; // 兼容旧字段
        photo.Notes = NullIfEmpty(tbNotes.Text);
        photo.RecognitionStatus = string.IsNullOrWhiteSpace(photo.AircraftModel) ? 0 : 2;

        await App.Database.UpdatePhotoAsync(photo);
        return true;
    }

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>沿视觉树向上判断 <paramref name="node"/> 是否为 <paramref name="ancestor"/> 的子孙。</summary>
    private static bool IsDescendantOf(DependencyObject? node, DependencyObject ancestor)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, ancestor)) return true;
            node = VisualTreeHelper.GetParent(node);
        }
        return false;
    }

    /// <summary>
    /// 从被点击的元素向上回溯，取第一个非空且属于候选列表内的字符串 DataContext。
    /// 用于 Tapped 命中判定：候选项数据就是字符串本身，其容器是 ListViewItem。
    /// </summary>
    private static string? FindRowDataContext(DependencyObject? node, DependencyObject listRoot)
    {
        while (node is not null && !ReferenceEquals(node, listRoot))
        {
            if (node is FrameworkElement { DataContext: string s } && !string.IsNullOrWhiteSpace(s))
            {
                return s;
            }
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
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
}
