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
            tbModel.DispatcherQueue.TryEnqueue(() =>
            {
                var focused = FocusManager.GetFocusedElement(xamlRoot) as DependencyObject;
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
