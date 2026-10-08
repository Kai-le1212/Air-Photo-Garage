using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AirPhotoGarage.Views;

/// <summary>
/// 首次启动引导：让用户决定照片存放（库根）目录。
///
/// 背景：MSIX 安装过程本身无法交互式询问路径，因此把「选择存放位置」放到
/// 首次启动时完成。若用户未做选择（关闭对话框或取消），则沿用默认目录
/// %LocalAppData%\AirPhotoGarage，保证应用始终可用。
/// </summary>
public static class FirstRunSetup
{
    /// <summary>默认库根目录：%LocalAppData%\AirPhotoGarage。</summary>
    public static string DefaultLibraryRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AirPhotoGarage");

    /// <summary>标记文件：存在即表示已完成首次启动引导（不再弹出）。</summary>
    private static string MarkerPath =>
        Path.Combine(DefaultLibraryRoot, ".first-run-done");

    /// <summary>是否已经完成过首次启动引导。</summary>
    public static bool IsCompleted
    {
        get
        {
            try { return File.Exists(MarkerPath); }
            catch { return false; }
        }
    }

    /// <summary>写入「已完成引导」标记。</summary>
    public static void MarkCompleted()
    {
        try
        {
            Directory.CreateDirectory(DefaultLibraryRoot);
            File.WriteAllText(MarkerPath, DateTime.Now.ToString("O"));
        }
        catch { /* 标记失败不影响使用，最多下次再问一遍 */ }
    }

    /// <summary>
    /// 弹出首次启动引导对话框，返回用户选择的库根目录。
    /// 返回 null 表示用户未做选择（应使用默认目录）。
    /// </summary>
    public static async Task<string?> PromptAsync(XamlRoot xamlRoot, nint windowHandle)
    {
        var picked = (string?)null;

        var pathBox = new TextBox
        {
            IsReadOnly = true,
            Text = DefaultLibraryRoot,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var browseButton = new Button
        {
            Content = "浏览…",
            MinWidth = 96,
        };

        var hint = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Text = "照片原图、缩略图和数据库都会保存在此目录下。"
                 + "\n若选择其他位置，请确保该磁盘空间充足。",
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(pathBox, 0);
        Grid.SetColumn(browseButton, 2);
        row.Children.Add(pathBox);
        row.Children.Add(browseButton);

        var panel = new StackPanel { Spacing = 12, MinWidth = 420 };
        panel.Children.Add(new TextBlock
        {
            Text = "首次使用，请选择照片存放位置（库根目录）。",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(row);
        panel.Children.Add(hint);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "欢迎使用 Air Photo Garage",
            Content = panel,
            PrimaryButtonText = "使用此位置",
            SecondaryButtonText = "使用默认位置",
            DefaultButton = ContentDialogButton.Primary,
        };

        browseButton.Click += async (_, _) =>
        {
            try
            {
                var picker = new FolderPicker();
                InitializeWithWindow.Initialize(picker, windowHandle);
                picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
                picker.FileTypeFilter.Add("*");   // FolderPicker 必须至少有一个 filter

                var folder = await picker.PickSingleFolderAsync();
                if (folder is not null)
                {
                    pathBox.Text = folder.Path;
                }
            }
            catch (Exception ex)
            {
                App.LogError("FirstRunSetup.PickFolder", ex);
            }
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            picked = pathBox.Text;
        }

        MarkCompleted();
        return picked;
    }
}
