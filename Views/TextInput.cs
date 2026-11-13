using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace AirPhotoGarage.Views;

/// <summary>
/// 输入框的统一格式化工具（注册号 / 二字码 / 航班号 / 机场代码都用它）。
///
/// <para>
/// <b>为什么不直接用 <see cref="TextBox.CharacterCasing"/></b>：实测在本项目的
/// WinUI 3 环境里对该输入路径<b>不生效</b> —— 真的键入 <c>b-9999</c> 后读回来仍是小写。
/// 所以统一改在 <c>TextChanged</c> 里回写，这条路已验证有效（筛选面板就是这么做才管用）。
/// </para>
///
/// <para>
/// 回写会再次触发 <c>TextChanged</c>，但那时文本已经合规、直接返回，不会递归。
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

    /// <summary>
    /// 代码类输入框的标配：<b>不弹中文输入法候选 / 关拼写检查 / 自动大写</b>。
    ///
    /// <para>
    /// 注册号、二字码、航班号、机场 IATA/ICAO 全是 ASCII，走中文输入法既慢又容易带进全角字符。
    /// 这里把输入作用域设成半角字母数字、关掉拼写检查与文本预测，并在 <c>TextChanged</c> 里强制大写。
    /// <b>中文内容（备注、航空公司名称、机场全称）不要用这个方法。</b>
    /// </para>
    /// </summary>
    public static void ApplyCode(TextBox box, bool upperCase = true)
    {
        box.IsSpellCheckEnabled = false;
        box.IsTextPredictionEnabled = false;
        box.InputScope = new InputScope
        {
            Names = { new InputScopeName(InputScopeNameValue.AlphanumericHalfWidth) },
        };

        if (!upperCase) return;

        box.TextChanged += (_, _) => ForceUpperCase(box);
        ForceUpperCase(box);
    }

    /// <summary>
    /// 注册号专用：不弹输入法 + 大写 + <b>首字母是 B 时自动补「-」</b>
    /// （<c>B1792</c> → <c>B-1792</c>；只看开头那个 B，不会把 <c>A6-EOC</c> 弄乱）。
    /// </summary>
    public static void ApplyRegistration(TextBox box)
    {
        ApplyCode(box);
        box.TextChanged += (_, _) => MaskRegistration(box);
        MaskRegistration(box);
    }

    private static void MaskRegistration(TextBox box)
    {
        var text = box.Text ?? string.Empty;
        if (text.Length < 2) return;

        if (char.ToUpperInvariant(text[0]) != 'B') return;
        if (text[1] == '-') return;
        if (!char.IsLetterOrDigit(text[1])) return;

        var caret = box.SelectionStart;
        var masked = "B-" + text[1..];
        box.Text = masked;
        // 补了一位「-」，光标跟着挪，否则下一字符会打到前面去
        box.SelectionStart = Math.Clamp(caret + 1, 0, masked.Length);
    }
}
