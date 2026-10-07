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
    private ImageSource? _thumbnail;

    [ObservableProperty]
    private bool _isThumbnailLoaded;

    public PhotoCardViewModel(Photo photo, IUiDispatcher uiDispatcher)
    {
        Photo = photo;
        _ui = uiDispatcher;
        _ = LoadThumbnailAsync();
    }

    public long Id => Photo.Id;

    public string? AircraftDisplay =>
        string.IsNullOrWhiteSpace(Photo.AircraftModel) ? "未填写机型" : Photo.AircraftModel;

    public string? RegistrationDisplay =>
        string.IsNullOrWhiteSpace(Photo.RegistrationNumber) ? "—" : Photo.RegistrationNumber;

    /// <summary>
    /// 注册号 + 拍摄时间的合并显示字符串。单独做成派生属性而不是用 <Run> 拼接，
    /// 是因为 WinAppSDK 2.3 编译器对 DataTemplate 内 <Run> 上的 x:Bind 处理存在 WMC1111 上下文丢失问题。
    /// </summary>
    public string? MetaDisplay =>
        $"{RegistrationDisplay}  ·  {ShotAtDisplay}";

    public string? AirportDisplay
    {
        get
        {
            // 优先显示 IATA + ICAO + Name 组合
            var code = Photo.AirportIata ?? Photo.AirportIcao ?? Photo.AirportCode;
            if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(Photo.AirportName))
                return $"{code} · {Photo.AirportName}";
            if (!string.IsNullOrWhiteSpace(code)) return code;
            if (!string.IsNullOrWhiteSpace(Photo.AirportName)) return Photo.AirportName;
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
        OnPropertyChanged(nameof(AircraftDisplay));
        OnPropertyChanged(nameof(RegistrationDisplay));
        OnPropertyChanged(nameof(MetaDisplay));
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
            Thumbnail = bmp;
            IsThumbnailLoaded = true;
        }
        catch
        {
            // ignore
        }
    }
}
