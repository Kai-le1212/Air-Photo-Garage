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
    private const int CellWidth = 120;
    private const int CellHeight = 90;

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

            var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var (previewPath, sourcePath) in candidates.Take(200))
            {
                var bitmap = await CoverImageLoader.LoadAsync(previewPath);
                if (bitmap is null) continue;

                var image = new Image
                {
                    Source = bitmap,
                    Width = CellWidth,
                    Height = CellHeight,
                    Stretch = Stretch.Uniform,   // 与全应用一致：预览不裁切
                };

                var cell = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    BorderThickness = new Thickness(1),
                    BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                    Background = (Brush)Application.Current.Resources["ControlAltFillColorTertiaryBrush"],
                    Child = image,
                };
                ToolTipService.SetToolTip(cell, System.IO.Path.GetFileName(sourcePath));

                var path = sourcePath;
                cell.Tapped += (_, _) =>
                {
                    chosen = path;
                    dialog.Hide();
                };

                strip.Children.Add(cell);
            }

            body.Children.Add(new ScrollViewer
            {
                Content = strip,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Auto,
            });
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

        try { await dialog.ShowAsync(); } catch { /* 已有对话框打开时忽略 */ }
    }
}
