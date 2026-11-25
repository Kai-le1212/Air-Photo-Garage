using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AirPhotoGarage.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace AirPhotoGarage.Views;

/// <summary>
/// 「选头图」的共用选择器 —— 三个分组页与照片墙的注册号窗格都调这一份。
///
/// <para>
/// 两条来源，对应「从已导入目录里查找」和「自己导入」：
/// <list type="number">
/// <item>对话框里直接列出<b>该分组已导入的照片</b>（缩略图条），点一张即可 ——
/// 这是最常用的路径，不用在文件对话框里翻目录；</item>
/// <item>「从文件导入…」按钮 → 标准文件选择器，选任意位置的任意图片。</item>
/// </list>
/// 两者最终都交给 <see cref="Services.CoverService"/>：图片会被规范化成小尺寸 PNG
/// 复制进 <c>Covers/</c>，<b>不写数据库</b>。
/// </para>
/// </summary>
internal static class CoverSourcePicker
{
    /// <summary>
    /// 弹出选头图对话框。
    /// </summary>
    /// <param name="candidates">该分组已导入的照片：(预览用缩略图, 实际用于生成头图的原图)。</param>
    /// <returns>选定的<b>图片文件路径</b>；取消时返回 null。</returns>
    public static async Task<string?> PickAsync(XamlRoot? xamlRoot, string title,
        IReadOnlyList<(string PreviewPath, string SourcePath)> candidates)
    {
        if (xamlRoot is null) return null;

        string? chosen = null;

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            PrimaryButtonText = "从文件导入…",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        Views.AppTheme.Apply(dialog);

        var body = new StackPanel { Spacing = 10 };

        if (candidates.Count == 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = "该分组还没有导入过照片，可以从文件里选一张。",
                TextWrapping = TextWrapping.Wrap,
            });
        }
        else
        {
            body.Children.Add(new TextBlock
            {
                Text = "选一张作为头图：",
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });

            // 横向卡片条：和数据模板配合，像 WinUI Gallery 首页那种并排大卡片
            var items = new List<CoverCandidate>();
            foreach (var (previewPath, sourcePath) in candidates.Take(200))
            {
                var bitmap = await CoverImageLoader.LoadAsync(previewPath);
                if (bitmap is null) continue;
                items.Add(new CoverCandidate(bitmap, System.IO.Path.GetFileName(sourcePath), sourcePath));
            }

            var grid = new GridView
            {
                ItemsSource = items,
                ItemTemplate = (DataTemplate)Application.Current.Resources["CoverCandidateTpl"],
                SelectionMode = ListViewSelectionMode.None,
                IsItemClickEnabled = true,
            };
            ToolTipService.SetToolTip(grid, "点一张作为头图");

            // 单行横排 + 横向滚动、不要纵向滚动
            grid.ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                "<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
                "<ItemsStackPanel Orientation='Horizontal' />" +
                "</ItemsPanelTemplate>");
            Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(grid, ScrollBarVisibility.Hidden);
            Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollBarVisibility(grid, ScrollBarVisibility.Disabled);
            Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollMode(grid, ScrollMode.Enabled);
            Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollMode(grid, ScrollMode.Disabled);

            grid.ItemClick += (_, e) =>
            {
                if (e.ClickedItem is not CoverCandidate picked) return;
                chosen = picked.SourcePath;
                dialog.Hide();
            };

            // 两端翻页箭头（Gallery「Recently visited」那种）：只在该方向还有内容时出现
            var leftChevron = MakeChevron(0xE76B, HorizontalAlignment.Left);
            var rightChevron = MakeChevron(0xE76C, HorizontalAlignment.Right);

            var host = new Grid { Height = 132, HorizontalAlignment = HorizontalAlignment.Stretch };
            grid.Padding = new Thickness(38, 0, 38, 0);   // 首尾卡片不压到箭头上
            host.Children.Add(grid);
            host.Children.Add(leftChevron);
            host.Children.Add(rightChevron);

            ScrollViewer? inner = null;

            void SyncChevrons()
            {
                if (inner is null) return;
                var canScroll = inner.ScrollableWidth > 1;
                leftChevron.Visibility = canScroll && inner.HorizontalOffset > 1
                    ? Visibility.Visible : Visibility.Collapsed;
                rightChevron.Visibility = canScroll && inner.HorizontalOffset < inner.ScrollableWidth - 1
                    ? Visibility.Visible : Visibility.Collapsed;
            }

            grid.Loaded += (_, _) =>
            {
                inner = FindScrollViewer(grid);
                if (inner is null) return;
                inner.ViewChanged += (_, _) => SyncChevrons();
                SyncChevrons();
            };

            const double page = 232 * 2;   // 两格多一点
            leftChevron.Click += (_, _) => inner?.ChangeView(
                Math.Max(0, inner.HorizontalOffset - page), null, null, false);
            rightChevron.Click += (_, _) => inner?.ChangeView(
                Math.Min(inner.ScrollableWidth, inner.HorizontalOffset + page), null, null, false);

            body.Children.Add(host);
        }

        dialog.Content = body;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            return await PickFromFileAsync();
        }

        return chosen;
    }

    /// <summary>标准文件选择器。WinUI 3 必须显式挂到窗口上，否则一定抛异常。</summary>
    public static async Task<string?> PickFromFileAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            ViewMode = PickerViewMode.Thumbnail,
        };

        foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tif", ".tiff" })
        {
            picker.FileTypeFilter.Add(ext);
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        var file = await picker.PickSingleFileAsync();
        return string.IsNullOrEmpty(file?.Path) ? null : file.Path;
    }

    /// <summary>两端翻页箭头：半透明圆角底 + 白箭头。</summary>
    private static Button MakeChevron(int glyph, HorizontalAlignment side)
    {
        var button = new Button
        {
            Content = new FontIcon
            {
                Glyph = char.ConvertFromUtf32(glyph),   // E76B / E76C：Segoe Fluent Icons 的 Chevron
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 14,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            Width = 30,
            Height = 96,
            Padding = new Thickness(0),
            HorizontalAlignment = side,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0),
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xE6, 48, 48, 48)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Visibility = Visibility.Collapsed,
            IsTabStop = false,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            button, side == HorizontalAlignment.Left ? "向前翻" : "向后翻");
        return button;
    }

    /// <summary>取 GridView / ListView 内部那个 ScrollViewer（翻页箭头要对它 ChangeView）。</summary>
    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            if (FindScrollViewer(child) is { } nested) return nested;
        }
        return null;
    }

    /// <summary>图片读不了/编码失败时给一句人话提示。</summary>
    public static async Task ShowFailedAsync(XamlRoot? xamlRoot)
    {
        if (xamlRoot is null) return;

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "这张图片不能用",
            Content = "可能格式不受支持，或者文件已经损坏。换一张常见格式（JPG / PNG / WebP）的图片试试。",
            CloseButtonText = "知道了",
        };
        Views.AppTheme.Apply(dialog);

        try { await dialog.ShowAsync(); } catch { /* 已有对话框打开时忽略 */ }
    }
}

/// <summary>选头图的一张候选照片。</summary>
public sealed class CoverCandidate
{
    public CoverCandidate(Microsoft.UI.Xaml.Media.ImageSource image, string caption, string sourcePath)
    {
        Image = image;
        Caption = caption;
        SourcePath = sourcePath;

        // 铺满卡片、不留边；圆角由外层 Border 裁
        ImageBrush = new Microsoft.UI.Xaml.Media.ImageBrush
        {
            ImageSource = image,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
        };
    }

    public Microsoft.UI.Xaml.Media.ImageSource Image { get; }

    /// <summary>卡片背景用的画刷（圆角裁切 + 铺满）。</summary>
    public Microsoft.UI.Xaml.Media.ImageBrush ImageBrush { get; }

    /// <summary>卡片下方的文件名。</summary>
    public string Caption { get; }

    /// <summary>真正用于生成头图的原图路径。</summary>
    public string SourcePath { get; }
}
