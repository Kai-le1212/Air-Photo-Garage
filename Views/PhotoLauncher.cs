using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;

namespace AirPhotoGarage.Views;

/// <summary>
/// 用<b>系统默认程序</b>打开照片原图。
///
/// <para>
/// 预览图放大了也看不清细节（尤其是判读注册号），点一下交给默认看图软件
/// （或 Photoshop / Lightroom）最省事。打包应用必须用
/// <see cref="StorageFile"/> + <see cref="Launcher"/>，直接 <c>Process.Start</c> 会被拦。
/// </para>
/// </summary>
internal static class PhotoLauncher
{
    public static async Task OpenAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            await Dialogs.AlertAsync("打不开原图", "找不到文件，可能已被移动或删除。");
            return;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            await Launcher.LaunchFileAsync(file);
        }
        catch (Exception ex)
        {
            App.LogError($"打开原图失败 {path}", ex);
            await Dialogs.AlertAsync("打不开原图", ex.Message);
        }
    }
}
