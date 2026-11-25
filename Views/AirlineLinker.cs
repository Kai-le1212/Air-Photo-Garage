using System;
using Microsoft.UI.Xaml.Controls;

namespace AirPhotoGarage.Views;

/// <summary>
/// 「航空公司名称 / 二字码(IATA) / 三字码(ICAO) / 航班号」<b>互相补齐</b>：
///
/// <list type="bullet">
/// <item>填名称 → 带出二字码 + 三字码；</item>
/// <item>填二字码或三字码 → 带出名称 + 对方代码；</item>
/// <item>填航班号（<c>CX390</c>）→ 取前两位 <c>CX</c> → 带出名称与两个代码。</item>
/// </list>
///
/// <para>
/// 导入向导、照片编辑、批量填写共用这一份。用一个 <c>syncing</c> 闸门防止互相触发死循环。
/// </para>
/// </summary>
internal static class AirlineLinker
{
    /// <summary><paramref name="flightBox"/> 可为 null（没有航班号字段时）。</summary>
    public static void Attach(
        AutoSuggestBox nameBox, TextBox codeBox, TextBox? icaoBox = null, TextBox? flightBox = null)
    {
        var catalog = App.Airlines;
        var syncing = false;

        void Fill(string? name, string? code, string? icao)
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
            if (icaoBox is not null
                && !string.IsNullOrEmpty(icao)
                && !string.Equals(icaoBox.Text, icao, StringComparison.OrdinalIgnoreCase))
            {
                icaoBox.Text = icao;
            }

            // 航班号：把航司代码填进去，用户只需要补后面的数字
            if (flightBox is not null && !string.IsNullOrEmpty(code))
            {
                var flight = flightBox.Text ?? string.Empty;
                if (flight.Length == 0)
                {
                    flightBox.Text = code;
                }
                else if (flight.Length >= 2
                         && char.IsAsciiLetter(flight[0])
                         && char.IsAsciiLetter(flight[1])
                         && !flight[..2].Equals(code, StringComparison.OrdinalIgnoreCase))
                {
                    flightBox.Text = code + flight[2..];
                }
            }

            syncing = false;
        }

        // 名称 → 两个代码（并给名称框挂候选）
        nameBox.TextChanged += (_, e) =>
        {
            if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            nameBox.ItemsSource = catalog.SearchNames(nameBox.Text);

            if (syncing) return;
            if (catalog.CodeOf(nameBox.Text) is not { } code) return;

            Fill(null, code, catalog.IcaoOf(nameBox.Text));
        };

        nameBox.SuggestionChosen += (_, e) =>
        {
            if (e.SelectedItem is not string picked) return;
            nameBox.Text = picked;
            if (syncing) return;
            if (catalog.CodeOf(picked) is not { } code) return;
            Fill(null, code, catalog.IcaoOf(picked));
        };

        // 二字码 → 名称 + 三字码
        codeBox.TextChanged += (_, _) =>
        {
            TextInput.ForceUpperCase(codeBox);

            if (syncing) return;
            var code = (codeBox.Text ?? string.Empty).Trim();
            if (code.Length != 2) return;

            var name = catalog.NameOf(code);
            if (name is null) return;

            Fill(name, null, catalog.IcaoOf(name));
        };

        if (icaoBox is null) return;

        // 三字码 → 名称 + 二字码
        icaoBox.TextChanged += (_, _) =>
        {
            TextInput.ForceUpperCase(icaoBox);

            if (syncing) return;
            var icao = (icaoBox.Text ?? string.Empty).Trim();
            if (icao.Length != 3) return;

            var name = catalog.NameOfIcao(icao);
            if (name is null) return;

            Fill(name, catalog.CodeOf(name), null);
        };

        if (flightBox is null) return;

        // 航班号 → 前两位当二字码 → 带出名称与两个代码
        flightBox.TextChanged += (_, _) =>
        {
            TextInput.ForceUpperCase(flightBox);

            if (syncing) return;

            var flight = (flightBox.Text ?? string.Empty).Trim();
            if (flight.Length < 2) return;

            var code = flight[..2];
            if (code[0] is < 'A' or > 'Z' || code[1] is < 'A' or > 'Z') return;

            var name = catalog.NameOf(code);
            if (name is null) return;

            Fill(name, code, catalog.IcaoOf(name));
        };
    }
}
