using Microsoft.UI.Xaml;

namespace AirPhotoGarage.Views;

/// <summary>
/// 当前应用主题的<b>单一来源</b>。
///
/// <para>
/// 主题是设在窗口根元素上的（<c>MainWindow.Content.RequestedTheme</c>），
/// 而 <see cref="Microsoft.UI.Xaml.Controls.ContentDialog"/> 是弹层，
/// <b>不继承</b>窗口根元素的主题 —— 它只认 <c>Application.RequestedTheme</c>（默认浅色）。
/// 结果就是「深色界面里弹出雪白对话框、浅色字几乎看不见」。
/// 所以每个对话框创建后都要显式 <see cref="Apply"/> 一下。
/// </para>
/// </summary>
internal static class AppTheme
{
    /// <summary>当前主题；0=系统默认 / 1=浅色 / 2=深色 由 MainWindow 换算成 ElementTheme。</summary>
    public static ElementTheme Current { get; private set; } = ElementTheme.Default;

    /// <summary>是否应视为深色（Default 时按系统判断）。</summary>
    public static bool IsDark => Current switch
    {
        ElementTheme.Dark => true,
        ElementTheme.Light => false,
        _ => Application.Current.RequestedTheme == ApplicationTheme.Dark,
    };

    public static void Set(ElementTheme theme) => Current = theme;

    /// <summary>把当前主题套到弹层元素上（对话框、自定义弹层都要调）。</summary>
    public static void Apply(FrameworkElement element) => element.RequestedTheme = Current;

    /// <summary>套好主题再 Show（内联创建的对话框用这个，免得漏掉）。</summary>
    public static async System.Threading.Tasks.Task<Microsoft.UI.Xaml.Controls.ContentDialogResult>
        ShowAsync(Microsoft.UI.Xaml.Controls.ContentDialog dialog)
    {
        Apply(dialog);
        return await dialog.ShowAsync();
    }
}
