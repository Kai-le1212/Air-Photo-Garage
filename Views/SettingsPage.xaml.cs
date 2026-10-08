using System;
using System.IO;
using System.Threading.Tasks;
using AirPhotoGarage.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AirPhotoGarage.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = App.SettingsViewModel;
    }

    /// <summary>
    /// 在用户选定（或新建）一个目录下创建 database 子文件夹，并把它设为库根目录。
    /// </summary>
    private async void OnCreateDatabaseFolderClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");   // FolderPicker 必须至少有一个 filter

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        var target = Path.Combine(folder.Path, "database");
        try
        {
            Directory.CreateDirectory(target);
        }
        catch (Exception ex)
        {
            await ShowInfoAsync("创建失败", $"无法创建目录：\n{target}\n\n{ex.Message}");
            return;
        }

        await ApplyNewLibraryRootAsync(target, $"已在下列位置创建 database 文件夹：\n{target}");
    }

    /// <summary>直接选择一个已有文件夹作为库根目录。</summary>
    private async void OnChooseLibraryFolderClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        await ApplyNewLibraryRootAsync(folder.Path, $"已选择新的库根目录：\n{folder.Path}");
    }

    /// <summary>恢复为默认库根目录（%LocalAppData%\AirPhotoGarage）。</summary>
    private async void OnResetLibraryRootClick(object sender, RoutedEventArgs e)
    {
        var def = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AirPhotoGarage");

        if (ViewModel.IsCurrentLibraryRoot(def))
        {
            await ShowInfoAsync("无需更改", "当前已经是默认库根目录。");
            return;
        }

        await ApplyNewLibraryRootAsync(def, $"已恢复默认库根目录：\n{def}");
    }

    /// <summary>
    /// 应用新的库根目录：写入配置 + 提示重启。
    /// 由于 Database / Importer 实例在启动时创建，运行时切换不安全，故只持久化配置。
    /// </summary>
    private async Task ApplyNewLibraryRootAsync(string path, string message)
    {
        if (ViewModel.IsCurrentLibraryRoot(path))
        {
            await ShowInfoAsync("无需更改", "该目录已作为当前库根目录。");
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex)
        {
            await ShowInfoAsync("设置失败", $"目录不可用：\n{path}\n\n{ex.Message}");
            return;
        }

        ViewModel.SetLibraryRoot(path);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "库位置已更新",
            Content = $"{message}\n\n更改将在重启应用后生效。原有照片不会自动迁移，如需保留请手动拷贝。",
            PrimaryButtonText = "立即重启",
            CloseButtonText = "稍后",
            DefaultButton = ContentDialogButton.Primary,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            RestartApp();
        }
    }

    private async Task ShowInfoAsync(string title, string content)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = content,
            CloseButtonText = "知道了",
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    /// <summary>
    /// 重启应用。打包应用（有包标识）用 AUMID 走 explorer 启动；失败则退回 RestartAgent。
    /// </summary>
    private static void RestartApp()
    {
        try
        {
            // WinUI 3 打包场景：通过 Microsoft.Windows.AppLifecycle 请求重启
            var args = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent();
            _ = args;
            Microsoft.Windows.AppLifecycle.AppInstance.Restart(string.Empty);
        }
        catch
        {
            // 非打包或 API 不可用：退而求其次，提示用户手动重启
            Application.Current.Exit();
        }
    }
}
