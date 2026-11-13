using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace AirPhotoGarage.Views;

/// <summary>
/// 用默认浏览器打开 Flightradar24 的「该注册号」页面。
///
/// <para>
/// 网址形如 <c>https://www.flightradar24.com/data/aircraft/B-329H</c>。
/// 打包应用必须走 <see cref="Launcher.LaunchUriAsync"/>，直接 Process.Start 会被拦。
/// </para>
/// </summary>
internal static class FlightRadar
{
    private const string BaseUrl = "https://www.flightradar24.com/data/aircraft/";

    public static string? UrlFor(string? registration)
    {
        var reg = (registration ?? string.Empty).Trim().ToUpperInvariant();
        // 注册号只可能由字母数字和「-」组成，解析一下更稳（防止空格/斜杠进 URL）
        reg = reg.Replace(" ", string.Empty).Replace("/", string.Empty);
        return reg.Length == 0 ? null : BaseUrl + Uri.EscapeDataString(reg);
    }

    /// <summary>打开该注册号的 FR24 页面；注册号为空时提示而不是静默失败。</summary>
    public static async Task OpenAsync(string? registration)
    {
        var url = UrlFor(registration);
        if (url is null)
        {
            await Dialogs.AlertAsync("还没有注册号", "先填好注册号，才能按它去 Flightradar24 查这架飞机。");
            return;
        }

        try
        {
            await Launcher.LaunchUriAsync(new Uri(url));
        }
        catch (Exception ex)
        {
            App.LogError($"打开 Flightradar24 失败 {url}", ex);
            await Dialogs.AlertAsync("打不开浏览器", ex.Message);
        }
    }

    /// <summary>做一个统一外观的「在 Flightradar24 查看」链接按钮。</summary>
    public static HyperlinkButton CreateLink(Func<string?> registration)
    {
        var link = new HyperlinkButton
        {
            Content = "在 Flightradar24 查看",
            Padding = new Thickness(0, 2, 0, 2),
            FontSize = 12,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(link, "在 Flightradar24 查看");
        ToolTipService.SetToolTip(link, "用默认浏览器打开这架飞机在 FR24 的资料页");

        link.Click += async (_, _) => await OpenAsync(registration());
        return link;
    }
}
