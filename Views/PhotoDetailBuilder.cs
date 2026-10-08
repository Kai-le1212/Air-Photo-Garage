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
/// 布局：左侧大图，右侧「基本信息 / EXIF 参数」双列，底部跨列展示文件信息、
/// AI 识别结果、路径，以及解析 <c>exif_json</c> 后的全部原始条目。
/// </para>
/// </summary>
internal static class PhotoDetailBuilder
{
    /// <summary>构建详情弹窗的完整可滚动内容。</summary>
    public static async Task<ScrollViewer> BuildContentAsync(Photo photo)
    {
        var img = await CreatePreviewImageAsync(photo);

        // ---- 右上：基本元数据（全字段，含空值占位） ----
        var basicInfo = new StackPanel { Spacing = 4, MinWidth = 240 };
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
        var exifInfo = new StackPanel { Spacing = 4, MinWidth = 240 };
        AddRowAlways(exifInfo, "相机厂商", photo.CameraMake);
        AddRowAlways(exifInfo, "相机型号", photo.CameraModel);
        AddRowAlways(exifInfo, "镜头", photo.LensModel);
        AddRowAlways(exifInfo, "焦距", photo.FocalLength.HasValue ? $"{photo.FocalLength.Value:0.##} mm" : null);
        AddRowAlways(exifInfo, "光圈", photo.Aperture);
        AddRowAlways(exifInfo, "快门", photo.ShutterSpeed);
        AddRowAlways(exifInfo, "ISO", photo.Iso?.ToString());
        AddRowAlways(exifInfo, "GPS 纬度", photo.Latitude?.ToString("0.######"));
        AddRowAlways(exifInfo, "GPS 经度", photo.Longitude?.ToString("0.######"));

        // ---- 左右双列 ----
        var rightGrid = new Grid();
        rightGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rightGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        rightGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(basicInfo, 0);
        Grid.SetColumn(exifInfo, 2);
        rightGrid.Children.Add(basicInfo);
        rightGrid.Children.Add(exifInfo);

        // ---- 底部：文件信息（全字段） ----
        var footerInfo = new StackPanel { Spacing = 4, Margin = new Thickness(0, 12, 0, 0) };
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

        // ---- 整体 Grid：左大图 + 右上双列 + 底部跨列 ----
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(540) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

        Grid.SetColumn(img, 0);
        Grid.SetRow(img, 0);
        body.Children.Add(img);

        Grid.SetColumn(rightGrid, 2);
        Grid.SetRow(rightGrid, 0);
        body.Children.Add(rightGrid);

        Grid.SetColumnSpan(footerInfo, 3);
        Grid.SetRow(footerInfo, 2);
        body.Children.Add(footerInfo);

        return new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 600,
        };
    }

    /// <summary>加载缩略图（缺失时回退原图）。失败时返回占位文字。</summary>
    private static async Task<FrameworkElement> CreatePreviewImageAsync(Photo photo)
    {
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
                return img;
            }
        }
        catch
        {
            // 读取失败时降级为占位提示，不影响其余字段展示
        }

        return new Border
        {
            Width = 540,
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

    /// <summary>
    /// 带标签的一行；空值也显示「—」，用于「展示全部参数」场景。
    /// </summary>
    public static void AddRowAlways(StackPanel parent, string label, string? value)
    {
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
