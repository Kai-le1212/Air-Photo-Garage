using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AirPhotoGarage.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace AirPhotoGarage.Views;

/// <summary>
/// 照片详情弹窗的内容构建器。
///
/// <para>
/// 设计与动机：照片墙（MainPage）和三个分组页（GroupedPhotoPage）都需要展示
/// 「全部参数 + 展开的原始 EXIF」。早期分组页只挑了 15 个关键字段，导致同一个
/// 页面语义在两处表现不一致。这里把构建逻辑收敛到一处，两个页面调用同一实现，
/// 避免再次分叉。
/// </para>
///
/// <para>
/// 布局：纵向堆叠——顶部大图，其下「基本信息 / EXIF 参数」两列，最后是文件信息、
/// AI 识别结果、路径，以及解析 <c>exif_json</c> 后的全部原始条目。
/// </para>
///
/// <para>
/// 为什么是纵向而不是「左图右参数」：<c>ContentDialog</c> 在内容期望宽度超过宿主
/// 可用宽度时不会收缩，而是整体右移、被窗口右边缘裁掉，用户看到的现象就是
/// 「详细信息没了」。纵向布局让各区域独占整行宽度，不依赖弹窗宽度的精确控制。
/// </para>
/// </summary>
internal static class PhotoDetailBuilder
{
    /// <summary>
    /// 构建详情弹窗的完整可滚动内容。
    /// </summary>
    public static async Task<ScrollViewer> BuildContentAsync(Photo photo)
    {
        var img = await CreatePreviewImageAsync(photo);

        // ---- 右上：基本元数据（全字段，含空值占位） ----
        var basicInfo = new StackPanel { Spacing = 4 };
        AddRowAlways(basicInfo, "ID", photo.Id.ToString());
        AddRowAlways(basicInfo, "机型", photo.AircraftModel);
        AddRowAlways(basicInfo, "注册号", photo.RegistrationNumber);
        AddRowAlways(basicInfo, "拍摄时间", photo.ShotAt?.ToString("yyyy-MM-dd HH:mm:ss"));
        AddRowAlways(basicInfo, "机场 IATA", photo.AirportIata);
        AddRowAlways(basicInfo, "机场 ICAO", photo.AirportIcao);
        AddRowAlways(basicInfo, "机场名称", photo.AirportName);
        AddRowAlways(basicInfo, "机场代码 (旧)", photo.AirportCode);
        AddRowAlways(basicInfo, "备注", photo.Notes);

        // ---- 右下：EXIF 详细信息（全字段） ----
        var exifInfo = new StackPanel { Spacing = 4 };
        AddRowAlways(exifInfo, "相机厂商", photo.CameraMake);
        AddRowAlways(exifInfo, "相机型号", photo.CameraModel);
        AddRowAlways(exifInfo, "镜头", photo.LensModel);
        AddRowAlways(exifInfo, "焦距", photo.FocalLength.HasValue ? $"{photo.FocalLength.Value:0.##} mm" : null);
        AddRowAlways(exifInfo, "光圈", photo.Aperture);
        AddRowAlways(exifInfo, "快门", photo.ShutterSpeed);
        AddRowAlways(exifInfo, "ISO", photo.Iso?.ToString());
        AddRowAlways(exifInfo, "GPS 纬度", photo.Latitude?.ToString("0.######"));
        AddRowAlways(exifInfo, "GPS 经度", photo.Longitude?.ToString("0.######"));

        // ---- 底部：文件信息（全字段） ----
        var footerInfo = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        AddRowAlways(footerInfo, "文件大小", FormatFileSize(photo.FileSize));
        AddRowAlways(footerInfo, "导入时间", photo.ImportedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        AddRowAlways(footerInfo, "识别状态", photo.RecognitionStatus switch
        {
            0 => "0 · 未识别",
            1 => "1 · 已识别",
            2 => "2 · 用户已修正",
            _ => photo.RecognitionStatus.ToString(),
        });
        AddRowAlways(footerInfo, "AI 识别机型", photo.RecognizedAircraftModel);
        AddRowAlways(footerInfo, "AI 置信度", photo.RecognitionConfidence is null
            ? null
            : photo.RecognitionConfidence.Value.ToString("P0"));
        AddRowAlways(footerInfo, "原图路径", photo.FilePath);
        AddRowAlways(footerInfo, "缩略图路径", photo.ThumbnailPath);

        // ---- 原始 EXIF（解析 exif_json 展开全部条目） ----
        var rawExif = ParseExifJson(photo.ExifJson);
        if (rawExif.Count > 0)
        {
            footerInfo.Children.Add(new TextBlock
            {
                Text = "原始 EXIF 数据",
                Margin = new Thickness(0, 10, 0, 2),
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            });
            foreach (var kv in rawExif)
            {
                AddRowAlways(footerInfo, kv.Key, kv.Value);
            }
        }

        // ---- 参数区：基本信息 / EXIF 参数 左右两列 ----
        var panels = new Grid();
        panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftPanel = new StackPanel { Spacing = 4 };
        leftPanel.Children.Add(SectionTitle("基本信息"));
        leftPanel.Children.Add(basicInfo);

        var rightPanel = new StackPanel { Spacing = 4 };
        rightPanel.Children.Add(SectionTitle("EXIF 参数"));
        rightPanel.Children.Add(exifInfo);

        Grid.SetColumn(leftPanel, 0);
        panels.Children.Add(leftPanel);
        Grid.SetColumn(rightPanel, 2);
        panels.Children.Add(rightPanel);

        // ---- 整体：上图 + 下双列参数 + 底部文件信息 ----
        // 采用纵向堆叠而不是「左图右参数」：
        // ContentDialog 的可用宽度由主题资源限制且不易精确控制，横向排布时
        // 图片列会把参数列挤出可视区（表现为右侧内容被裁掉）。纵向排布下
        // 各区域独占整行宽度，无论弹窗多宽多窄都不会互相挤压。
        var body = new StackPanel { Spacing = 16 };
        body.Children.Add(img);
        body.Children.Add(panels);
        body.Children.Add(footerInfo);

        // ---- 兜底：外层允许宽度不足时，把两列参数退化为单列 ----
        // 这里不写死宽度，仅限制面板的最小宽度，避免极窄窗口下文字被压成竖条。
        leftPanel.MinWidth = 200;
        rightPanel.MinWidth = 200;

        var host = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = body,
        };

        // 内容宽度上限。
        //
        // 实测：ContentDialog 在内容期望宽度超过宿主可用宽度时不会收缩，
        // 而是整体右移并被窗口右边缘裁掉——这正是「详细信息没了」的成因。
        // 因此这里显式给定一个足够保守的内容宽度（520），保证弹窗始终完整可见。
        // 纵向布局下单列 520 足以展示全部字段（长文本会自动换行）。
        host.MaxWidth = 520;
        host.Width = 520;

        return new ScrollViewer
        {
            Content = host,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            // 让内容随窗口高度自适应，同时保留上限避免超高屏撑满
            MaxHeight = 620,
        };
    }

    /// <summary>
    /// 构建「照片详情」弹窗（标题 / 按钮 / 内容装配）。
    ///
    /// <para>
    /// 宽度相关的踩坑记录，避免以后重复尝试无效方案：
    /// <list type="bullet">
    /// <item>设 <c>dialog.Resources["ContentDialogMaxWidth"]</c> <b>不生效</b>——模板不从实例资源取。</item>
    /// <item>设在 <c>Application.Resources</c> 顶层也 <b>不生效</b>——该键由
    /// XamlControlsResources 定义在 ThemeDictionaries 内，主题查找优先级更高。</item>
    /// <item>把内容做成 <c>Stretch</c> 会让弹窗跟着变宽并溢出窗口右边缘。</item>
    /// <item><b>可行方案</b>：内容用纵向布局，并给内容容器一个保守的固定宽度
    /// （见 <see cref="BuildContentAsync"/> 里的 520），让弹窗稳定落在可用宽度内。</item>
    /// </list>
    /// </para>
    /// </summary>
    public static ContentDialog CreateDialog(XamlRoot? xamlRoot, long photoId, UIElement content,
        string? primaryButtonText = null)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = $"照片详情 · #{photoId}",
            Content = content,
            CloseButtonText = "关闭",
            DefaultButton = primaryButtonText is null
                ? ContentDialogButton.Close
                : ContentDialogButton.Primary,
        };
        if (primaryButtonText is not null)
        {
            dialog.PrimaryButtonText = primaryButtonText;
        }

        return dialog;
    }

    /// <summary>参数分区的小标题。</summary>
    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        Margin = new Thickness(0, 0, 0, 2),
        Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
    };

    /// <summary>
    /// 加载缩略图（缺失时回退原图）。失败时返回占位文字。
    ///
    /// <para>
    /// 图片用 <c>Stretch.Uniform</c>，即整个画面完整显示、保持原比例，不裁切。
    /// 外面套一层带底色与描边的容器：夜景这类**边缘本身就很暗**的照片，
    /// 直接贴在深色弹窗背景上时四周会糊在一起，看着像被裁过。
    /// </para>
    /// </summary>
    private static async Task<FrameworkElement> CreatePreviewImageAsync(Photo photo)
    {
        var img = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxHeight = 360,
            MaxWidth = 620,
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
                return WrapPreview(img);
            }
        }
        catch
        {
            // 读取失败时降级为占位提示，不影响其余字段展示
        }

        return new Border
        {
            Height = 300,
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.Resources["ControlAltFillColorTertiaryBrush"],
            Child = new TextBlock
            {
                Text = "无法加载预览图",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            },
        };
    }

    /// <summary>给预览图加一圈边界，让画面的实际范围（含四周留边）一目了然。</summary>
    private static FrameworkElement WrapPreview(Image img) => new Border
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        CornerRadius = new CornerRadius(6),
        BorderThickness = new Thickness(1),
        BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
        Background = (Brush)Application.Current.Resources["ControlAltFillColorTertiaryBrush"],
        Child = img,
    };

    /// <summary>
    /// 带标签的一行；空值也显示「—」，用于「展示全部参数」场景。
    /// </summary>
    public static void AddRowAlways(StackPanel parent, string label, string? value)
    {
        var row = new Grid();
        // 标签列收窄到 88：内容整体宽度有限（弹窗约 520），
        // 标签太宽会把值列挤到没有空间换行。
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
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
            Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,   // 便于复制路径等长文本
            Foreground = string.IsNullOrWhiteSpace(value)
                ? (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
                : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
        };
        Grid.SetColumn(t1, 0);
        Grid.SetColumn(t2, 1);
        row.Children.Add(t1);
        row.Children.Add(t2);
        parent.Children.Add(row);
    }

    /// <summary>
    /// 解析 exif_json（扁平对象或嵌套对象/数组），展平成 键→值 列表。
    /// 解析失败时原样返回一行，避免信息丢失。
    /// </summary>
    public static List<KeyValuePair<string, string>> ParseExifJson(string? json)
    {
        var result = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            FlattenJson(doc.RootElement, null, result);
        }
        catch
        {
            result.Add(new KeyValuePair<string, string>("(原始)", json));
        }
        return result;
    }

    private static void FlattenJson(JsonElement el, string? prefix, List<KeyValuePair<string, string>> sink)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in el.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                    FlattenJson(prop.Value, key, sink);
                }
                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in el.EnumerateArray())
                {
                    FlattenJson(item, $"{prefix}[{i++}]", sink);
                }
                break;
            default:
                sink.Add(new KeyValuePair<string, string>(prefix ?? "(值)", el.ToString()));
                break;
        }
    }

    /// <summary>人类可读的文件大小。</summary>
    public static string FormatFileSize(long bytes)
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
}
