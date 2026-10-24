using System;
using Microsoft.UI.Xaml.Controls;

namespace AirPhotoGarage.Views;

/// <summary>
/// 把「航空公司名称」和「二字码」两个输入框<b>互相补齐</b>：
/// 填了名称就自动带出二字码，填了二字码就自动带出名称。
///
/// <para>
/// 照片编辑对话框与导入向导共用这一份逻辑，避免两处行为不一致。
/// 目录来自 <see cref="Services.AirlineCatalogService"/>（约 90 家常见公司）。
/// </para>
///
/// <para>
/// 用一个 <c>_syncing</c> 闸门防止互相触发死循环：程序化回填时闸门关着，
/// 对手方的 <c>TextChanged</c> 直接忽略。
/// </para>
/// </summary>
internal static class AirlineLinker
{
    public static void Attach(AutoSuggestBox nameBox, TextBox codeBox)
    {
        var catalog = App.Airlines;
        var syncing = false;

        // 名称 → 二字码（并给名称框挂候选）
        nameBox.TextChanged += (_, e) =>
        {
            if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            nameBox.ItemsSource = catalog.SearchNames(nameBox.Text);

            if (syncing) return;
            var code = catalog.CodeOf(nameBox.Text);
            if (code is null) return;

            syncing = true;
            codeBox.Text = code;
            syncing = false;
        };

        nameBox.SuggestionChosen += (_, e) =>
        {
            if (e.SelectedItem is not string picked) return;
            nameBox.Text = picked;
            if (syncing) return;
            var code = catalog.CodeOf(picked);
            if (code is null) return;
            syncing = true;
            codeBox.Text = code;
            syncing = false;
        };

        // 二字码 → 名称（二字码统一大写，最多两位）
        codeBox.TextChanged += (_, _) =>
        {
            // 大写 + 去空格（与注册号同样的处理方式：CharacterCasing 实测不生效，改为回写）
            var upper = (codeBox.Text ?? string.Empty).Trim().ToUpperInvariant();
            if (!string.Equals(codeBox.Text, upper, StringComparison.Ordinal))
            {
                var caret = codeBox.SelectionStart;
                codeBox.Text = upper;
                codeBox.SelectionStart = Math.Clamp(caret, 0, upper.Length);
            }

            if (syncing) return;
            var name = catalog.NameOf(upper);
            if (name is null) return;

            syncing = true;
            nameBox.Text = name;
            syncing = false;
        };
    }
}
