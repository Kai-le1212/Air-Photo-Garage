using System;
using Microsoft.UI.Xaml.Controls;

namespace AirPhotoGarage.Views;

/// <summary>
/// 输入框的强制格式化工具。
///
/// <para>
/// <b>为什么不直接用 <see cref="TextBox.CharacterCasing"/></b>：实测在本项目的
/// WinUI 3 环境里对该输入路径<b>不生效</b> —— 真的键入 <c>b-9999</c> 后读回来仍是小写。
/// 所以统一改在 <c>TextChanged</c> 里回写，这条路已验证有效（筛选面板就是这么做才管用）。
/// </para>
///
/// <para>
/// 回写会再次触发 <c>TextChanged</c>，但那时文本已经是大写、直接返回，不会递归。
/// 回写前记下光标位置、回写后恢复，避免光标跳到末尾。
/// </para>
/// </summary>
internal static class TextInput
{
    /// <summary>把输入框内容就地转成大写（保留光标位置）。</summary>
    public static void ForceUpperCase(TextBox box)
    {
        var text = box.Text ?? string.Empty;
        var upper = text.ToUpperInvariant();
        if (string.Equals(text, upper, StringComparison.Ordinal)) return;

        var caret = box.SelectionStart;
        box.Text = upper;
        box.SelectionStart = Math.Clamp(caret, 0, upper.Length);
    }
}
