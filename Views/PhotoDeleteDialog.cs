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
    /// <b>只从数据库移除记录，磁盘上的原图保留</b> —— 不会误删用户素材。
    /// 这里刻意只负责「确认 + 删库」，不碰任何 ViewModel 的集合：
    /// 照片墙与分组页各自的集合类型不同（平铺 / 分组 / 时间轴），
    /// 由调用方按自己的结构刷新。
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
            Content = summary.ToString() + "\n（仅从数据库移除，磁盘文件保留）",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return false;

        await App.Database.DeletePhotoAsync(photo.Id);
        return true;
    }
}
