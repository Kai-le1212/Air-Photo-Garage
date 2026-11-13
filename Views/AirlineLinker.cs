using System;
using Microsoft.UI.Xaml.Controls;

namespace AirPhotoGarage.Views;

/// <summary>
/// 「航空公司名称 / 二字码 / 航班号」三者<b>互相补齐</b>：
///
/// <list type="bullet">
/// <item>填名称 → 带出二字码；</item>
/// <item>填二字码 → 带出名称；</item>
/// <item>填航班号（<c>CX390</c>）→ 取出前两位 <c>CX</c> → 带出名称与二字码。</item>
/// </list>
///
/// <para>
/// 导入向导、照片编辑、批量填写共用这一份，避免三处行为不一致。
/// 目录来自 <see cref="Services.AirlineCatalogService"/>。
/// </para>
///
/// <para>
/// 用一个 <c>syncing</c> 闸门防止互相触发死循环：程序化回填时闸门关着，
/// 对手方的 <c>TextChanged</c> 直接忽略。
/// </para>
/// </summary>
internal static class AirlineLinker
{
    /// <summary>挂上三向联动；<paramref name="flightBox"/> 可为 null（没有航班号字段时）。</summary>
    public static void Attach(AutoSuggestBox nameBox, TextBox codeBox, TextBox? flightBox = null)
    {
        var catalog = App.Airlines;
        var syncing = false;

        void Fill(string? name, string? code)
        {
            syncing = true;
            if (!string.IsNullOrEmpty(name)
                && !string.Equals(nameBox.Text, name, StringComparison.Ordinal))
            {
                nameBox.Text = name;
            }
            if (!string.IsNullOrEmpty(code)
                && !string.Equals(codeBox.Text, code, StringComparison.OrdinalIgnoreCase))
            {
                codeBox.Text = code;
            }
            syncing = false;
        }

        // 名称 → 二字码（并给名称框挂候选）
        nameBox.TextChanged += (_, e) =>
        {
            if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            nameBox.ItemsSource = catalog.SearchNames(nameBox.Text);

            if (syncing) return;
            var code = catalog.CodeOf(nameBox.Text);
            if (code is null) return;

            Fill(null, code);
        };

        nameBox.SuggestionChosen += (_, e) =>
        {
            if (e.SelectedItem is not string picked) return;
            nameBox.Text = picked;
            if (syncing) return;
            var code = catalog.CodeOf(picked);
            if (code is null) return;
            Fill(null, code);
        };

        // 二字码 → 名称（二字码统一大写，最多两位）
        codeBox.TextChanged += (_, _) =>
        {
            TextInput.ForceUpperCase(codeBox);

            if (syncing) return;
            var code = (codeBox.Text ?? string.Empty).Trim();
            if (code.Length != 2) return;

            var name = catalog.NameOf(code);
            if (name is null) return;

            Fill(name, null);
        };

        if (flightBox is null) return;

        // 航班号 → 前两位当二字码 → 带出名称与二字码
        flightBox.TextChanged += (_, _) =>
        {
            TextInput.ForceUpperCase(flightBox);

            if (syncing) return;

            var flight = (flightBox.Text ?? string.Empty).Trim();
            if (flight.Length < 2) return;

            var code = flight[..2];
            if (!IsLetters(code)) return;

            var name = catalog.NameOf(code);
            if (name is null) return;

            Fill(name, code);
        };
    }

    private static bool IsLetters(string value)
    {
        foreach (var c in value)
        {
            if (c is < 'A' or > 'Z') return false;
        }
        return true;
    }
}
