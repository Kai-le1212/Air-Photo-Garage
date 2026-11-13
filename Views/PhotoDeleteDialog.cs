using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AirPhotoGarage.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AirPhotoGarage.Views;

/// <summary>
/// 照片删除确认（照片墙与机型 / 机场 / 注册号三个分组页共用）。
///
/// <para>
/// 抽出来的原因与 <see cref="PhotoEditDialog"/> 完全相同：此前这段逻辑只存在于
/// <c>MainPage</c> 的私有方法里，导致三个分组页的右键菜单<b>没有「删除」</b>。
/// 凡是「多个页面都该有」的照片操作，一律在这里实现一次、各处共用，
/// 避免再出现能力漂移。
/// </para>
/// </summary>
public static class PhotoDeleteDialog
{
    /// <summary>
    /// 弹出删除确认框；用户确认后从数据库删除该照片。
    /// </summary>
    /// <returns>是否真的执行了删除。调用方需据此刷新自己的集合。</returns>
    /// <remarks>
    /// 删除 = <b>记录 + 磁盘文件（原图 + 缩略图）一起删</b>。
    /// 早先只删记录、文件留在库里，结果攒出一堆「库里没有、磁盘上还在」的孤儿文件，
    /// 用户下次「扫描并补登记」又把它们加回来了 —— 删了等于没删。
    /// 这里仍只负责「确认 + 落库 + 删文件」，不碰任何 ViewModel 的集合：
    /// 照片墙与分组页各自的集合类型不同（平铺 / 分组 / 时间轴），由调用方刷新。
    /// </remarks>
    public static async Task<bool> ConfirmAndDeleteAsync(XamlRoot? xamlRoot, Photo photo)
    {
        var summary = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(photo.AircraftModel)) summary.AppendLine($"机型：{photo.AircraftModel}");
        if (!string.IsNullOrWhiteSpace(photo.RegistrationNumber)) summary.AppendLine($"注册号：{photo.RegistrationNumber}");
        if (photo.ShotAt.HasValue) summary.AppendLine($"拍摄时间：{photo.ShotAt:yyyy-MM-dd HH:mm}");
        if (summary.Length == 0) summary.AppendLine("(无元数据)");

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = $"确认删除照片 #{photo.Id}？",
            Content = summary.ToString() + "\n（记录与磁盘上的原图、缩略图都会被删除，不可恢复）",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        Views.AppTheme.Apply(dialog);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return false;

        await App.Database.DeletePhotoAsync(photo.Id);
        DeleteFiles(photo);
        return true;
    }

    /// <summary>
    /// 删掉原图与缩略图。文件被占用 / 已不存在都不影响删除结果（记录已经删了），
    /// 只写日志，不向上抛。
    /// </summary>
    private static void DeleteFiles(Photo photo)
    {
        DeleteFile(photo.FilePath);
        DeleteFile(photo.ThumbnailPath);
    }

    private static void DeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            App.LogError($"删除文件失败 {path}", ex);
        }
    }
}
