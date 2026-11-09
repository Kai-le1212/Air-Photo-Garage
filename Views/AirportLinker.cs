using System;
using Microsoft.UI.Xaml.Controls;

namespace AirPhotoGarage.Views;

/// <summary>
/// 机场三个字段<b>互相补齐</b>：IATA 三字码 / ICAO 四字码 / 机场全称。
///
/// <para>
/// 填三字码 → 自动带出四字码和全称；填四字码 → 带出三字码和全称；
/// 填全称（唯一匹配时）→ 带出两个代码。目录来自 <c>App.AirportCatalog</c>。
/// </para>
///
/// <para>
/// 照片编辑对话框与批量填写对话框共用这一份，避免两处行为不一致。
/// </para>
/// </summary>
internal static class AirportLinker
{
    public static void Attach(TextBox iata, TextBox icao, TextBox name)
    {
        iata.TextChanged += (_, _) =>
        {
            var text = (iata.Text ?? string.Empty).Trim().ToUpperInvariant();
            if (text.Length != 3) return;

            var airport = App.AirportCatalog.FindByIata(text);
            if (airport is null) return;

            SetIfDifferent(icao, airport.Icao);
            SetIfDifferent(name, airport.Name);
        };

        icao.TextChanged += (_, _) =>
        {
            var text = (icao.Text ?? string.Empty).Trim().ToUpperInvariant();
            if (text.Length != 4) return;

            var airport = App.AirportCatalog.FindByIcao(text);
            if (airport is null) return;

            SetIfDifferent(iata, airport.Iata);
            SetIfDifferent(name, airport.Name);
        };

        name.TextChanged += (_, _) =>
        {
            // 名称是自由文本，只有唯一匹配时才回填，避免把用户输入改乱
            var results = App.AirportCatalog.SearchByName(name.Text);
            if (results.Count != 1) return;

            SetIfDifferent(iata, results[0].Iata);
            SetIfDifferent(icao, results[0].Icao);
        };
    }

    private static void SetIfDifferent(TextBox box, string? value)
    {
        if (value is null) return;
        if (string.Equals(box.Text, value, StringComparison.Ordinal)) return;
        box.Text = value;
    }
}
