using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AirPhotoGarage.Views;

/// <summary>
/// 通用提示 / 确认对话框。
///
/// <para>
/// <c>ContentDialog</c> 必须挂 <c>XamlRoot</c>，集中在这里取 <see cref="App.Window"/> 的根，
/// 避免每个调用点各写一遍、漏了就是「弹窗不出现」。
/// </para>
/// </summary>
internal static class Dialogs
{
    private static XamlRoot? Root => App.Window?.Content?.XamlRoot;

    /// <summary>只有一个「知道了」的提示框。</summary>
    public static async Task AlertAsync(string title, string message)
    {
        var root = Root;
        if (root is null) return;

        await new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = message,
            CloseButtonText = "知道了",
            DefaultButton = ContentDialogButton.Close,
        }.ShowAsync();
    }

    /// <summary>
    /// 二次确认。<paramref name="defaultIsClose"/> 为 true 时默认落在「返回/继续」上，
    /// 防止手快一路回车把正在做的事取消掉。
    /// </summary>
    public static async Task<bool> ConfirmAsync(
        string title, string message, string primaryText, string closeText, bool defaultIsClose = true)
    {
        var root = Root;
        if (root is null) return false;

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = message,
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText,
            DefaultButton = defaultIsClose ? ContentDialogButton.Close : ContentDialogButton.Primary,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
