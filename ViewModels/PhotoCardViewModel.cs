using System;
using System.IO;
using System.Threading.Tasks;
using AirPhotoGarage.Models;
using AirPhotoGarage.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace AirPhotoGarage.ViewModels;

/// <summary>
/// 单张照片在照片墙上的展示模型。
/// 构造后会自动异步加载缩略图（从 <see cref="Photo.ThumbnailPath"/> 读取），
/// 通过 <see cref="Thumbnail"/> 暴露 <see cref="ImageSource"/> 供 XAML 绑定。
///
/// 派生属性（<see cref="AircraftDisplay"/>、<see cref="HasNoInfo"/> 等）一次计算，
/// 外部修改了底层 <see cref="Photo"/> 字段后必须调用 <see cref="RefreshDisplayProperties"/>
/// 触发 PropertyChanged，UI 才会刷新。
/// </summary>
public sealed partial class PhotoCardViewModel : ObservableObject
{
    private readonly IUiDispatcher _ui;

    public Photo Photo { get; }

    [ObservableProperty]
    public partial ImageSource? Thumbnail { get; set; }

    [ObservableProperty]
    public partial bool IsThumbnailLoaded { get; set; }

    /// <summary>
    /// 已加载缩略图的宽高比（宽 ÷ 高）。缩略图未就绪时为 0。
    /// 缩略图按 <c>DecodePixelHeight = 480</c> 解码，所以 <c>PixelWidth/PixelHeight</c>
    /// 直接就是原始比例，不必再去库里存一份尺寸。
    /// </summary>
    [ObservableProperty]
    public partial double ThumbnailAspect { get; set; }

    /// <summary>卡片宽度。必须与 XAML 里卡片根元素的 Width 保持一致。</summary>
    public const double CardWidth = 240;

    /// <summary>
    /// 卡片上是否显示文字信息（注册号 / 机型·时间 / 机场）。
    ///
    /// <para>
    /// 「注册号」页把这些文字挪到了日期节点的说明区，卡片只留图片；
    /// 「机型」「机场」页**保持原样**，文字仍印在卡片上 ——
    /// 那两页一个日期节点里可能混着不同飞机，全挪到说明区反而不好一一对应。
    /// </para>
    /// <para>由页面加载时一次性设定，因此不做变更通知（x:Bind 默认 OneTime 足够）。</para>
    /// </summary>
    public bool ShowCardText { get; set; } = true;

    /// <summary>卡片文字区的可见性（对应 <see cref="ShowCardText"/>）。</summary>
    public Microsoft.UI.Xaml.Visibility CardTextVisibility => ShowCardText
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>
    /// 卡片图片区应有的高度。
    ///
    /// <para>
    /// 让<b>框的高度跟着图片比例走</b>，而不是把图片塞进一个固定高度的框：
    /// 固定框 + Uniform 会在上下留出空带，UniformToFill 又会裁掉画面边缘 ——
    /// 两者用户都不接受。高度随比例走，图片正好填满，既不空也不裁。
    /// </para>
    /// <para>缩略图未就绪时退回 3:2 的占位高度，避免卡片高度先塌后跳。</para>
    /// </summary>
    public double CardImageHeight =>
        ThumbnailAspect > 0.01 ? CardWidth / ThumbnailAspect : CardWidth * 2.0 / 3.0;

    partial void OnThumbnailAspectChanged(double value) => OnPropertyChanged(nameof(CardImageHeight));

    public PhotoCardViewModel(Photo photo, IUiDispatcher uiDispatcher)
    {
        Photo = photo;
        _ui = uiDispatcher;
        _ = LoadThumbnailAsync();
    }

    public long Id => Photo.Id;

    /// <summary>
    /// 卡片<b>主标题</b>：统一显示注册号。
    ///
    /// <para>
    /// 为什么用注册号而不是机型：机型（如 "Airbus A330-300"）是<b>共享</b>的，
    /// 一次航展可能拍到十几架同型机；只有注册号唯一标识一架具体飞机。
    /// 照片墙 / 机型 / 机场 / 注册号四处视图统一用注册号做标题，
    /// 用户扫一眼就能对上"这是哪一架"。
    /// </para>
    /// </summary>
    public string PrimaryDisplay =>
        string.IsNullOrWhiteSpace(Photo.RegistrationNumber) ? "未填写注册号" : Photo.RegistrationNumber!;

    /// <summary>机型显示（缺失时给占位文案）。作为副信息使用。</summary>
    public string? AircraftDisplay =>
        string.IsNullOrWhiteSpace(Photo.AircraftModel) ? "未填写机型" : Photo.AircraftModel;

    public string? RegistrationDisplay =>
        string.IsNullOrWhiteSpace(Photo.RegistrationNumber) ? "—" : Photo.RegistrationNumber;

    /// <summary>
    /// 机场显示行。统一为「机场名 · 代码」顺序，与机场页的分组行保持一致。
    /// <para>
    /// 此前这里是「代码 · 机场名」，而机场页分组行是「机场名 · 代码」——
    /// 同一份数据在应用里出现两种顺序，来回切换时读起来割裂。
    /// 名字比代码更好认，所以统一以名字开头；名字缺失时才退回代码。
    /// </para>
    /// </summary>
    public string? AirportDisplay
    {
        get
        {
            var code = Photo.AirportIata ?? Photo.AirportIcao ?? Photo.AirportCode;
            if (!string.IsNullOrWhiteSpace(Photo.AirportName) && !string.IsNullOrWhiteSpace(code))
                return $"{Photo.AirportName} · {code}";
            if (!string.IsNullOrWhiteSpace(Photo.AirportName)) return Photo.AirportName;
            if (!string.IsNullOrWhiteSpace(code)) return code;
            return "未填写机场";
        }
    }

    /// <summary>
    /// 拍摄时间显示。EXIF DateTimeOriginal 已按相机本地时间原样存为 UTC+0，
    /// 这里直接 ToString，不再 ToLocalTime 二次转换（否则会偏移时区）。
    /// </summary>
    public string ShotAtDisplay =>
        Photo.ShotAt.HasValue
            ? Photo.ShotAt.Value.ToString("yyyy-MM-dd HH:mm")
            : "未知时间";

    /// <summary>当机型与注册号都未填写时为 true，用于决定是否叠加「未提供信息」覆盖层。</summary>
    public bool HasNoInfo =>
        string.IsNullOrWhiteSpace(Photo.AircraftModel) &&
        string.IsNullOrWhiteSpace(Photo.RegistrationNumber);

    /// <summary>
    /// 通知所有派生属性重新计算。编辑/导入完成后调用，避免 UI 仍显示旧值。
    /// </summary>
    public void RefreshDisplayProperties()
    {
        OnPropertyChanged(nameof(PrimaryDisplay));
        OnPropertyChanged(nameof(AircraftDisplay));
        OnPropertyChanged(nameof(RegistrationDisplay));
        OnPropertyChanged(nameof(AirportDisplay));
        OnPropertyChanged(nameof(ShotAtDisplay));
        OnPropertyChanged(nameof(HasNoInfo));
    }

    private async Task LoadThumbnailAsync()
    {
        var path = Photo.ThumbnailPath ?? Photo.FilePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        try
        {
            // 后台读取字节，避免阻塞 UI
            byte[] bytes;
            using (var fs = File.OpenRead(path))
            using (var ms = new MemoryStream())
            {
                await fs.CopyToAsync(ms);
                bytes = ms.ToArray();
            }

            // 切回 UI 线程；BitmapImage.SetSourceAsync 必须在 UI 线程执行
            _ui.TryEnqueue(() => _ = ApplyThumbnailOnUiThread(bytes));
        }
        catch
        {
            // ignore
        }
    }

    private async Task ApplyThumbnailOnUiThread(byte[] bytes)
    {
        try
        {
            // 按 image.md 最佳实践：设置 DecodePixelHeight 让 BitmapImage
            // 只解码到目标高度，避免将 4K 原图完整解码到内存。
            var bmp = new BitmapImage
            {
                DecodePixelHeight = 480,
            };
            using var ms = new MemoryStream(bytes);
            await bmp.SetSourceAsync(ms.AsRandomAccessStream());

            // 解码完成后 PixelWidth/PixelHeight 即可用；转成宽高比给卡片算图片区高度。
            // 放在赋值 Thumbnail 之前，让 CardImageHeight 与图片同一次通知一起生效。
            if (bmp.PixelWidth > 0 && bmp.PixelHeight > 0)
            {
                ThumbnailAspect = (double)bmp.PixelWidth / bmp.PixelHeight;
            }

            Thumbnail = bmp;
            IsThumbnailLoaded = true;
        }
        catch
        {
            // ignore
        }
    }
}
