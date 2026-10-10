using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AirPhotoGarage.Models;
using AirPhotoGarage.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace AirPhotoGarage.ViewModels;

/// <summary>
/// 批量导入向导的 ViewModel。
/// 流程：
/// 1. 用户从文件选择器选 N 张图，传入 <see cref="StartAsync"/>。
/// 2. ViewModel 在后台调用 <see cref="IPhotoImportService.PrepareImportAsync"/>，得到 Photo 列表（已含缩略图与 EXIF）。
/// 3. 用户逐张确认/补全「机型 / 注册号 / 机场 / 备注」。
/// 4. 完成后通过 <see cref="CompletedPhotos"/> 返回所有 Photo，由 GarageViewModel 写入数据库。
/// </summary>
public sealed partial class ImportWizardViewModel : ObservableObject
{
    private readonly IPhotoImportService _importer;
    private readonly IUiDispatcher _ui;
    private readonly IAirportCatalogService _airportCatalog;
    private readonly IAircraftCatalogService _aircraftCatalog;

    private readonly List<Photo> _photos = new();
    private readonly List<ImageSource?> _thumbnails = new();

    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string StatusMessage { get; set; } = "";
    [ObservableProperty] public partial int CurrentIndex { get; set; }

    // 当前编辑字段
    [ObservableProperty] public partial string AircraftModel { get; set; } = "";
    [ObservableProperty] public partial string RegistrationNumber { get; set; } = "";
    [ObservableProperty] public partial string AirportIata { get; set; } = "";
    [ObservableProperty] public partial string AirportIcao { get; set; } = "";
    [ObservableProperty] public partial string AirportName { get; set; } = "";
    [ObservableProperty] public partial string Notes { get; set; } = "";

    // 派生显示
    [ObservableProperty] public partial ImageSource? CurrentThumbnail { get; set; }

    /// <summary>
    /// 当前缩略图的宽高比（宽 ÷ 高）。未就绪时为 0。
    /// 缩略图按 <c>DecodePixelHeight = 480</c> 解码，所以 <c>PixelWidth/PixelHeight</c>
    /// 就是原始比例。
    /// </summary>
    [ObservableProperty] public partial double ThumbnailAspect { get; set; }

    /// <summary>预览区宽度。必须与 XAML 里左栏列宽一致。</summary>
    public const double PreviewWidth = 240;

    /// <summary>
    /// 预览区应有的高度：按图片比例算，图片正好填满 —— 不裁切、上下也不留空。
    /// 上限 360，避免竖幅照片把对话框撑高。
    /// </summary>
    public double PreviewHeight =>
        ThumbnailAspect > 0.01
            ? Math.Min(PreviewWidth / ThumbnailAspect, 360)
            : PreviewWidth * 2.0 / 3.0;

    partial void OnThumbnailAspectChanged(double value) => OnPropertyChanged(nameof(PreviewHeight));
    [ObservableProperty] public partial string StepText { get; set; } = "";
    [ObservableProperty] public partial string NextButtonText { get; set; } = "下一张 >";
    [ObservableProperty] public partial string ShotAtText { get; set; } = "";
    [ObservableProperty] public partial string ExifHint { get; set; } = "";

    /// <summary>
    /// 用户是否通过「完成导入」按钮正常结束向导。
    /// false 表示用户点了「取消」/「✕」——此时不应把照片写入数据库。
    /// </summary>
    public bool IsConfirmed { get; private set; }

    /// <summary>
    /// 用于让调用方（GarageViewModel）等待用户走完整个向导。
    /// 用户在 UI 上点「完成导入」或「取消」时被 SetResult。
    /// </summary>
    private TaskCompletionSource<bool>? _completionTcs;

    public ImportWizardViewModel(
        IPhotoImportService importer,
        IUiDispatcher uiDispatcher,
        IAirportCatalogService airportCatalog,
        IAircraftCatalogService aircraftCatalog)
    {
        _importer = importer;
        _ui = uiDispatcher;
        _airportCatalog = airportCatalog;
        _aircraftCatalog = aircraftCatalog;
    }

    public int TotalCount => _photos.Count;

    public bool CanGoBack => CurrentIndex > 0;

    public bool CanCopyPrevious => CurrentIndex > 0;

    public bool CanGoNext => CurrentIndex < _photos.Count - 1;

    /// <summary>完成向导后，向调用方返回的所有 Photo 对象（已含用户填写的元数据）。</summary>
    public IReadOnlyList<Photo> CompletedPhotos => _photos;

    /// <summary>机型候选（供 UI 下拉使用，来自内置机型库 + 已入库机型）。</summary>
    public IReadOnlyList<string> AircraftSuggestions(string keyword)
        => _aircraftCatalog.Search(keyword);

    /// <summary>全部机型（无关键字时的完整列表）。</summary>
    public IReadOnlyList<string> AllAircraftModels => _aircraftCatalog.All;

    /// <summary>
    /// 预处理照片（复制 + 缩略图 + EXIF），然后打开向导等待用户逐张填写。
    /// <para>
    /// 注意：<b>本方法不返回后即代表导入完成</b>。它只负责"打开向导"；
    /// 用户填写完毕后需由调用方 await <see cref="WaitForCompletionAsync"/> 取得确认结果。
    /// </para>
    /// </summary>
    public async Task<bool> StartAsync(IReadOnlyList<string> sourceFiles)
    {
        if (sourceFiles is null || sourceFiles.Count == 0) return false;

        IsConfirmed = false;   // 新一轮导入，重置确认标志
        _completionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        IsBusy = true;
        StatusMessage = $"准备导入 {sourceFiles.Count} 张...";
        try
        {
            _photos.Clear();
            _thumbnails.Clear();

            // 1. 预处理：复制 + 缩略图 + EXIF
            int succeeded = 0, failed = 0;
            foreach (var src in sourceFiles)
            {
                try
                {
                    var photo = await _importer.PrepareImportAsync(src);
                    _photos.Add(photo);
                    succeeded++;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PrepareImport failed {src}: {ex}");
                    failed++;
                }
            }

            if (_photos.Count == 0)
            {
                StatusMessage = $"预处理全部失败：{failed} 张";
                IsOpen = false;
                _completionTcs.TrySetResult(false);
                return false;
            }

            // 2. 在 UI 线程上为每张照片创建缩略图 ImageSource
            foreach (var photo in _photos)
            {
                _thumbnails.Add(null);
                _ = LoadThumbnailFor(photo, _photos.IndexOf(photo));
            }

            CurrentIndex = 0;
            IsOpen = true;
            LoadCurrentToFields();
            StatusMessage = failed == 0
                ? $"已加载 {succeeded} 张，请逐张确认信息"
                : $"已加载 {succeeded} 张（{failed} 张失败）";
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 等待用户在向导中做出最终决定（完成导入 / 取消）。
    /// 返回 true 表示用户确认导入，可安全读取 <see cref="CompletedPhotos"/> 并入库。
    /// </summary>
    public Task<bool> WaitForCompletionAsync()
    {
        if (_completionTcs is null) return Task.FromResult(false);
        return _completionTcs.Task;
    }

    partial void OnCurrentIndexChanged(int value)
    {
        LoadCurrentToFields();
    }

    /// <summary>把当前 Photo 的字段加载到可编辑属性。</summary>
    private void LoadCurrentToFields()
    {
        if (_photos.Count == 0) return;
        var p = _photos[CurrentIndex];

        AircraftModel = p.AircraftModel ?? "";
        RegistrationNumber = p.RegistrationNumber ?? "";
        AirportIata = p.AirportIata ?? "";
        AirportIcao = p.AirportIcao ?? "";
        AirportName = p.AirportName ?? "";
        Notes = p.Notes ?? "";
        CurrentThumbnail = _thumbnails[CurrentIndex];

        StepText = $"第 {CurrentIndex + 1} / {_photos.Count} 张";
        NextButtonText = CurrentIndex == _photos.Count - 1 ? "完成导入" : "下一张 >";

        // EXIF DateTimeOriginal 已是相机本地时间原值，不要 ToLocalTime 二次转换
        ShotAtText = p.ShotAt.HasValue
            ? p.ShotAt.Value.ToString("yyyy-MM-dd HH:mm")
            : "未读取到拍摄时间";

        var exifParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.CameraModel)) exifParts.Add(p.CameraModel);
        if (!string.IsNullOrWhiteSpace(p.LensModel)) exifParts.Add(p.LensModel);
        if (p.FocalLength.HasValue) exifParts.Add($"{p.FocalLength.Value:0}mm");
        if (!string.IsNullOrWhiteSpace(p.Aperture)) exifParts.Add(p.Aperture);
        if (!string.IsNullOrWhiteSpace(p.ShutterSpeed)) exifParts.Add(p.ShutterSpeed);
        if (p.Iso.HasValue) exifParts.Add($"ISO {p.Iso}");
        ExifHint = exifParts.Count == 0 ? "" : "  ·  " + string.Join(" · ", exifParts);

        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanCopyPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }

    /// <summary>把当前可编辑属性写回 Photo 对象。</summary>
    private void SaveFieldsToCurrent()
    {
        if (_photos.Count == 0) return;
        var p = _photos[CurrentIndex];
        p.AircraftModel = EmptyToNull(AircraftModel);
        p.RegistrationNumber = EmptyToNull(RegistrationNumber);
        p.AirportIata = EmptyToNull(AirportIata?.ToUpperInvariant());
        p.AirportIcao = EmptyToNull(AirportIcao?.ToUpperInvariant());
        p.AirportCode = p.AirportIata ?? p.AirportIcao; // 兼容旧字段
        p.AirportName = EmptyToNull(AirportName);
        p.Notes = EmptyToNull(Notes);
    }

    private static string? EmptyToNull(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ---- 机场三字段联动 ----
    // 任一字段变化时，回填其他两个（仅在值非空且与目录精确匹配时）。

    partial void OnAirportIataChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 3) return;
        var a = _airportCatalog.FindByIata(value);
        if (a is null) return;
        if (!string.Equals(AirportIcao, a.Icao, StringComparison.OrdinalIgnoreCase))
            AirportIcao = a.Icao ?? "";
        if (!string.Equals(AirportName, a.Name, StringComparison.Ordinal))
            AirportName = a.Name;
    }

    partial void OnAirportIcaoChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 4) return;
        var a = _airportCatalog.FindByIcao(value);
        if (a is null) return;
        if (!string.Equals(AirportIata, a.Iata, StringComparison.OrdinalIgnoreCase))
            AirportIata = a.Iata ?? "";
        if (!string.Equals(AirportName, a.Name, StringComparison.Ordinal))
            AirportName = a.Name;
    }

    partial void OnAirportNameChanged(string value)
    {
        var results = _airportCatalog.SearchByName(value);
        if (results.Count != 1) return;
        var a = results[0];
        if (!string.Equals(AirportIata, a.Iata, StringComparison.OrdinalIgnoreCase))
            AirportIata = a.Iata ?? "";
        if (!string.Equals(AirportIcao, a.Icao, StringComparison.OrdinalIgnoreCase))
            AirportIcao = a.Icao ?? "";
    }

    [RelayCommand]
    private void Previous()
    {
        if (CurrentIndex <= 0) return;
        SaveFieldsToCurrent();
        CurrentIndex--;
    }

    [RelayCommand]
    private void Next()
    {
        SaveFieldsToCurrent();
        if (CurrentIndex < _photos.Count - 1)
        {
            CurrentIndex++;
        }
        else
        {
            // 最后一张：代表用户确认完成导入
            IsConfirmed = true;
            IsOpen = false;
            _completionTcs?.TrySetResult(true);
        }
    }

    /// <summary>一键把上一张已填写的信息复制到当前张。</summary>
    [RelayCommand]
    private void CopyFromPrevious()
    {
        if (CurrentIndex <= 0) return;
        var prev = _photos[CurrentIndex - 1];
        AircraftModel = prev.AircraftModel ?? "";
        RegistrationNumber = prev.RegistrationNumber ?? "";
        AirportIata = prev.AirportIata ?? "";
        AirportIcao = prev.AirportIcao ?? "";
        AirportName = prev.AirportName ?? "";
        Notes = prev.Notes ?? "";
    }

    /// <summary>跳过本张，不修改字段并前进。</summary>
    [RelayCommand]
    private void Skip()
    {
        Next();
    }

    [RelayCommand]
    private void Cancel()
    {
        // 取消：不回写字段、不入库，直接关闭。
        IsConfirmed = false;
        IsOpen = false;
        _completionTcs?.TrySetResult(false);
    }

    private async Task LoadThumbnailFor(Photo photo, int index)
    {
        var path = photo.ThumbnailPath ?? photo.FilePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        try
        {
            byte[] bytes;
            using (var fs = File.OpenRead(path))
            using (var ms = new MemoryStream())
            {
                await fs.CopyToAsync(ms);
                bytes = ms.ToArray();
            }

            _ui.TryEnqueue(() => _ = ApplyThumbnailOnUiThread(bytes, index));
        }
        catch
        {
            // ignore
        }
    }

    private async Task ApplyThumbnailOnUiThread(byte[] bytes, int index)
    {
        try
        {
            // 按 image.md 最佳实践：设置 DecodePixelHeight 让 BitmapImage
            // 只解码到目标高度，避免完整解码。
            var bmp = new BitmapImage { DecodePixelHeight = 480 };
            using var ms = new MemoryStream(bytes);
            await bmp.SetSourceAsync(ms.AsRandomAccessStream());

            // 解码后 PixelWidth/PixelHeight 可用；转成宽高比给预览区算高度
            if (bmp.PixelWidth > 0 && bmp.PixelHeight > 0)
            {
                ThumbnailAspect = (double)bmp.PixelWidth / bmp.PixelHeight;
            }

            if (index < _thumbnails.Count)
            {
                _thumbnails[index] = bmp;
            }
            if (index == CurrentIndex)
            {
                CurrentThumbnail = bmp;
            }
        }
        catch
        {
            // ignore
        }
    }
}
