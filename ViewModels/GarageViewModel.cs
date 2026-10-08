using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AirPhotoGarage.Models;
using AirPhotoGarage.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirPhotoGarage.ViewModels;

/// <summary>
/// 照片墙主视图模型。
/// 负责：维护筛选条件、刷新照片列表、协调导入流程。
/// 所有数据库/IO 调用均在后台线程，避免阻塞 UI。
/// </summary>
public sealed partial class GarageViewModel : ObservableObject
{
    private readonly IDatabaseService _db;
    private readonly IPhotoImportService _importer;
    private readonly IUiDispatcher _ui;

    public GarageViewModel(IDatabaseService db, IPhotoImportService importer, IUiDispatcher uiDispatcher)
    {
        _db = db;
        _importer = importer;
        _ui = uiDispatcher;
        Photos = new ObservableCollection<PhotoCardViewModel>();
    }

    public ObservableCollection<PhotoCardViewModel> Photos { get; }

    // ---- 筛选条件（双向绑定） ----

    [ObservableProperty] public partial string? Keyword { get; set; }
    [ObservableProperty] public partial string? AircraftModelFilter { get; set; }
    [ObservableProperty] public partial string? RegistrationFilter { get; set; }
    [ObservableProperty] public partial string? AirportCodeFilter { get; set; }
    [ObservableProperty] public partial DateTimeOffset? ShotFrom { get; set; }
    [ObservableProperty] public partial DateTimeOffset? ShotTo { get; set; }

    public IReadOnlyList<string> SortOptions { get; } = new[]
    {
        "拍摄时间倒序", "拍摄时间正序", "导入时间倒序", "机型 A→Z", "注册号 A→Z"
    };

    [ObservableProperty] public partial int SortIndex { get; set; }

    [ObservableProperty] public partial string StatusMessage { get; set; } = "准备就绪";
    [ObservableProperty] public partial bool IsBusy { get; set; }

    // ---- 自动补全候选 ----
    public ObservableCollection<string> AircraftSuggestions { get; } = new();
    public ObservableCollection<string> RegistrationSuggestions { get; } = new();
    public ObservableCollection<string> AirportSuggestions { get; } = new();

    partial void OnSortIndexChanged(int value) => _ = RefreshAsync();

    partial void OnKeywordChanged(string? value) { /* 由 ApplyFilter 统一触发 */ }
    partial void OnAircraftModelFilterChanged(string? value) { }
    partial void OnRegistrationFilterChanged(string? value) { }
    partial void OnAirportCodeFilterChanged(string? value) { }
    partial void OnShotFromChanged(DateTimeOffset? value) { }
    partial void OnShotToChanged(DateTimeOffset? value) { }

    public void InvalidateFilter() => _ = RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "正在加载...";
        try
        {
            var filter = BuildFilter();
            var items = await _db.QueryPhotosAsync(filter, skip: 0, take: 5000);

            Photos.Clear();
            foreach (var p in items)
            {
                Photos.Add(new PhotoCardViewModel(p, _ui));
            }
            StatusMessage = $"共 {Photos.Count} 张照片";

            await RefreshSuggestionsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ImportPhotosAsync(IReadOnlyList<string> filePaths)
    {
        if (filePaths is null || filePaths.Count == 0) return;
        if (IsBusy) return;

        // 1. 打开导入向导（预处理照片），并等待用户在界面上完成或取消
        var wizard = App.ImportWizardViewModel;
        var started = await wizard.StartAsync(filePaths);
        if (!started) return;

        var confirmed = await wizard.WaitForCompletionAsync();
        if (!confirmed || wizard.CompletedPhotos.Count == 0) return;

        // 2. 用户确认后，把填好元数据的照片写入数据库
        IsBusy = true;
        var imported = 0;
        var failed = 0;
        StatusMessage = $"正在写入数据库...";
        try
        {
            foreach (var photo in wizard.CompletedPhotos)
            {
                try
                {
                    photo.RecognitionStatus = string.IsNullOrWhiteSpace(photo.AircraftModel) ? 0 : 2;
                    await _db.InsertPhotoAsync(photo);
                    imported++;
                }
                catch (Exception ex)
                {
                    failed++;
                    System.Diagnostics.Debug.WriteLine($"DB insert failed: {ex}");
                }
            }
            StatusMessage = $"导入完成：成功 {imported} 张，失败 {failed} 张";
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeletePhotoAsync(PhotoCardViewModel? card)
    {
        if (card is null) return;
        try
        {
            await _db.DeletePhotoAsync(card.Id);
            Photos.Remove(card);
            StatusMessage = $"已删除照片 #{card.Id}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"删除失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearFilter()
    {
        Keyword = null;
        AircraftModelFilter = null;
        RegistrationFilter = null;
        AirportCodeFilter = null;
        ShotFrom = null;
        ShotTo = null;
        SortIndex = 0;
        _ = RefreshAsync();
    }

    /// <summary>
    /// 由 View 在 Loaded 后调用一次，触发首屏数据加载。
    /// </summary>
    public Task InitializeAsync() => RefreshAsync();

    private PhotoFilter BuildFilter()
    {
        var order = SortIndex switch
        {
            1 => PhotoSortOrder.ShotAtAscending,
            2 => PhotoSortOrder.ImportedAtDescending,
            3 => PhotoSortOrder.AircraftModelAscending,
            4 => PhotoSortOrder.RegistrationAscending,
            _ => PhotoSortOrder.ShotAtDescending,
        };
        return new PhotoFilter
        {
            Keyword = Keyword,
            AircraftModel = AircraftModelFilter,
            RegistrationNumber = RegistrationFilter,
            AirportCode = AirportCodeFilter,
            ShotFrom = ShotFrom,
            ShotTo = ShotTo,
            SortOrder = order,
        };
    }

    private async Task RefreshSuggestionsAsync()
    {
        try
        {
            var aircraft = await _db.GetDistinctValuesAsync("aircraft_model");
            var regs = await _db.GetDistinctValuesAsync("registration_number");
            var airports = await _db.GetDistinctValuesAsync("airport_code");
            _ui.TryEnqueue(() =>
            {
                AircraftSuggestions.Clear();
                foreach (var a in aircraft) AircraftSuggestions.Add(a);
                RegistrationSuggestions.Clear();
                foreach (var r in regs) RegistrationSuggestions.Add(r);
                AirportSuggestions.Clear();
                foreach (var ap in airports) AirportSuggestions.Add(ap);
            });
        }
        catch
        {
            // ignore
        }
    }
}
