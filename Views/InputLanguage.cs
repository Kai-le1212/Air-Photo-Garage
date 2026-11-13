using System;
using System.Runtime.InteropServices;

namespace AirPhotoGarage.Views;

/// <summary>
/// 让「代码类输入框」在获得焦点时<b>自动切到英文键盘布局</b>，失焦后切回原布局。
///
/// <para>
/// WinUI 3 没有「切换输入法」的 API，这里走 Windows 老办法：
/// 给窗口投一条 <c>WM_INPUTLANGCHANGEREQUEST</c>，请系统换成「英语(美国)」布局。
/// 前提是系统里装了美式键盘（中文 Windows 默认都有）；没装则这条消息无声失败，
/// 此时仍由字符过滤兜底（中文/全角字符会被丢掉）。
/// </para>
///
/// <para>
/// 只在 <b>真正需要 ASCII</b> 的框上挂（注册号 / 二字码 / 航班号 / 机场代码 / 机型），
/// 备注与航空公司名称不挂，中文输入照旧。
/// </para>
/// </summary>
internal static class InputLanguage
{
    private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
    private const string EnglishLayout = "00000409";   // en-US

    private static IntPtr _previousLayout = IntPtr.Zero;
    private static bool _switched;

    /// <summary>切到英文布局，并记住当前布局以便恢复。</summary>
    public static void SwitchToEnglish()
    {
        if (_switched) return;

        var hwnd = App.WindowHandle;
        if (hwnd == IntPtr.Zero) return;

        try
        {
            var current = GetKeyboardLayout(GetWindowThreadProcessId(hwnd, out _));
            var english = LoadKeyboardLayout(EnglishLayout, KLF_ACTIVATE);
            if (english == IntPtr.Zero) return;

            _previousLayout = current;
            _switched = true;
            PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, english);
        }
        catch (Exception ex)
        {
            App.LogError("InputLanguage.SwitchToEnglish", ex);
        }
    }

    /// <summary>恢复进入前的键盘布局。</summary>
    public static void Restore()
    {
        if (!_switched) return;
        _switched = false;

        var hwnd = App.WindowHandle;
        if (hwnd == IntPtr.Zero || _previousLayout == IntPtr.Zero) return;

        try
        {
            PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, _previousLayout);
        }
        catch (Exception ex)
        {
            App.LogError("InputLanguage.Restore", ex);
        }
        finally
        {
            _previousLayout = IntPtr.Zero;
        }
    }

    private const uint KLF_ACTIVATE = 0x00000001;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
