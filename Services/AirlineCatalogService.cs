using System;
using System.Collections.Generic;
using System.Linq;

namespace AirPhotoGarage.Services;

/// <summary>
/// 航空公司目录：<b>名称 ↔ 二字码(IATA) ↔ 三字码(ICAO)</b> 互相补齐。
///
/// <para>
/// 存三项：名称、二字码、三字码。填任一项，其余两项自动带出。
/// </para>
/// </summary>
public interface IAirlineCatalogService
{
    /// <summary>全部航空公司名称（字母序）。</summary>
    IReadOnlyList<string> AllNames { get; }

    /// <summary>按关键字搜名称（前缀优先）。</summary>
    IReadOnlyList<string> SearchNames(string keyword);

    /// <summary>按关键字搜二字码（前缀优先），返回形如「CX」的码。</summary>
    IReadOnlyList<string> SearchCodes(string keyword);

    /// <summary>名称 → 二字码；目录里没有返回 null。</summary>
    string? CodeOf(string? name);

    /// <summary>二字码 → 名称；目录里没有返回 null。</summary>
    string? NameOf(string? code);

    /// <summary>名称 → 三字码(ICAO)；目录里没有返回 null。</summary>
    string? IcaoOf(string? name);

    /// <summary>三字码(ICAO) → 名称；目录里没有返回 null。</summary>
    string? NameOfIcao(string? icao);

    /// <summary>按关键字搜三字码（前缀优先）。</summary>
    IReadOnlyList<string> SearchIcaoCodes(string keyword);
}

public sealed class AirlineCatalogService : IAirlineCatalogService
{
    /// <summary>（名称, 二字码 IATA, 三字码 ICAO）</summary>
    private static readonly (string Name, string Code, string Icao)[] Seed =
    {
        // ---- 中国内地 ----
        ("中国国际航空", "CA", "CCA"), ("中国东方航空", "MU", "CES"),
        ("中国南方航空", "CZ", "CSN"),
        ("海南航空", "HU", "CHH"), ("厦门航空", "MF", "CXA"),
        ("深圳航空", "ZH", "CSZ"),
        ("四川航空", "3U", "CSC"), ("山东航空", "SC", "CDG"),
        ("春秋航空", "9C", "CQH"),
        ("吉祥航空", "HO", "DKH"), ("天津航空", "GS", "GCR"),
        ("首都航空", "JD", "CBJ"),
        ("上海航空", "FM", "CSH"), ("中国联合航空", "KN", "CUA"),
        ("成都航空", "EU", "UEA"),
        ("河北航空", "NS", "HBH"), ("青岛航空", "QW", "QDA"),
        ("长龙航空", "GJ", "CDC"),
        ("西部航空", "PN", "CHB"), ("奥凯航空", "BK", "OKA"),
        ("华夏航空", "G5", "HXA"),
        ("西藏航空", "TV", "TBA"), ("昆明航空", "KY", "KNA"),
        ("瑞丽航空", "DR", "RLH"),
        ("东海航空", "DZ", "EPA"), ("乌鲁木齐航空", "UQ", "CUH"),
        ("顺丰航空", "O3", "CSS"), ("中国邮政航空", "CF", "CYZ"),
        ("圆通航空", "YG", "HYT"),

        // ---- 中国港澳台 ----
        ("国泰航空", "CX", "CPA"), ("港龙航空", "KA", "HDA"),
        ("香港航空", "HX", "CRK"),
        ("香港快运", "UO", "HKE"), ("澳门航空", "NX", "AMU"),
        ("中华航空", "CI", "CAL"), ("长荣航空", "BR", "EVA"),
        ("星宇航空", "JX", "SJX"),
        ("立荣航空", "B7", "UIA"), ("华信航空", "AE", "MDA"),

        // ---- 亚洲 ----
        ("新加坡航空", "SQ", "SIA"), ("酷航", "TR", "SCO"),
        ("马来西亚航空", "MH", "MAS"),
        ("亚洲航空", "AK", "AXM"), ("泰国国际航空", "TG", "THA"),
        ("泰越捷航空", "VZ", "TVJ"),
        ("越南航空", "VN", "HVN"), ("越捷航空", "VJ", "VJC"),
        ("菲律宾航空", "PR", "PAL"),
        ("宿务太平洋航空", "5J", "CEB"), ("印尼鹰航", "GA", "GIA"),
        ("狮航", "JT", "LNI"),
        ("全日空", "NH", "ANA"), ("日本航空", "JL", "JAL"),
        ("乐桃航空", "MM", "APJ"),
        ("大韩航空", "KE", "KAL"), ("韩亚航空", "OZ", "AAR"),
        ("济州航空", "7C", "JJA"),
        ("印度航空", "AI", "AIC"), ("靛蓝航空", "6E", "IGO"),

        // ---- 中东 / 土耳其 ----
        ("阿联酋航空", "EK", "UAE"), ("阿提哈德航空", "EY", "ETD"),
        ("卡塔尔航空", "QR", "QTR"),
        ("土耳其航空", "TK", "THY"), ("沙特航空", "SV", "SVA"),
        ("阿曼航空", "WY", "OMA"),

        // ---- 欧洲 ----
        ("汉莎航空", "LH", "DLH"), ("法国航空", "AF", "AFR"),
        ("英国航空", "BA", "BAW"),
        ("荷兰皇家航空", "KL", "KLM"), ("瑞士国际航空", "LX", "SWR"),
        ("奥地利航空", "OS", "AUA"),
        ("北欧航空", "SK", "SAS"), ("芬兰航空", "AY", "FIN"),
        ("西班牙国家航空", "IB", "IBE"),
        ("葡萄牙航空", "TP", "TAP"), ("意大利航空", "AZ", "ITY"),
        ("俄罗斯航空", "SU", "AFL"),
        ("波兰航空", "LO", "LOT"), ("爱尔兰航空", "EI", "EIN"),

        // ---- 美洲 / 大洋洲 / 非洲 ----
        ("美国航空", "AA", "AAL"), ("联合航空", "UA", "UAL"),
        ("达美航空", "DL", "DAL"),
        ("西南航空", "WN", "SWA"), ("加拿大航空", "AC", "ACA"),
        ("墨西哥航空", "AM", "AMX"),
        ("澳洲航空", "QF", "QFA"), ("维珍澳洲航空", "VA", "VOZ"),
        ("新西兰航空", "NZ", "ANZ"),
        ("埃塞俄比亚航空", "ET", "ETH"), ("南非航空", "SA", "SAA"),
        ("埃及航空", "MS", "MSR"),

        // ---- 货运 ----
        ("联邦快递", "FX", "FDX"), ("联合包裹", "5X", "UPS"),
        ("极地航空货运", "PO", "PAC"),
        ("卡利塔航空", "K4", "CKS"),

        // ---- 其它常见 ----
        ("易捷航空", "U2", "EZY"), ("九元航空", "AQ", "JYH"),
        ("重庆航空", "OQ", "CQN"), ("长安航空", "9H", "CGN"),
    };

    private readonly Dictionary<string, string> _byName;      // 名称 → IATA
    private readonly Dictionary<string, string> _byIcao;      // 名称 → ICAO
    private readonly Dictionary<string, string> _byCode;      // IATA → 名称
    private readonly Dictionary<string, string> _byIcaoToName; // ICAO → 名称
    private readonly List<string> _names;

    public AirlineCatalogService()
    {
        _byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _byIcao = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _byCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _byIcaoToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, code, icao) in Seed)
        {
            _byName[name] = code;
            _byCode[code] = name;
            if (string.IsNullOrEmpty(icao)) continue;
            _byIcao[name] = icao;
            _byIcaoToName[icao] = name;
        }

        _names = _byName.Keys.OrderBy(n => n, StringComparer.CurrentCulture).ToList();
    }

    public IReadOnlyList<string> AllNames => _names;

    public IReadOnlyList<string> SearchNames(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return _names;
        var k = keyword.Trim();

        return _names
            .Where(n => n.Contains(k, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n.StartsWith(k, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Take(20)
            .ToList();
    }

    public IReadOnlyList<string> SearchCodes(string keyword)
    {
        var all = _byCode.Keys.OrderBy(c => c, StringComparer.Ordinal).ToList();
        if (string.IsNullOrWhiteSpace(keyword)) return all;

        var k = keyword.Trim();
        return all
            .Where(c => c.StartsWith(k, StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .ToList();
    }

    public string? CodeOf(string? name) =>
        !string.IsNullOrWhiteSpace(name) && _byName.TryGetValue(name.Trim(), out var code) ? code : null;

    public string? NameOf(string? code) =>
        !string.IsNullOrWhiteSpace(code) && _byCode.TryGetValue(code.Trim(), out var name) ? name : null;

    public string? IcaoOf(string? name) =>
        !string.IsNullOrWhiteSpace(name) && _byIcao.TryGetValue(name.Trim(), out var icao) ? icao : null;

    public string? NameOfIcao(string? icao) =>
        !string.IsNullOrWhiteSpace(icao) && _byIcaoToName.TryGetValue(icao.Trim(), out var name) ? name : null;

    public IReadOnlyList<string> SearchIcaoCodes(string keyword)
    {
        var all = _byIcaoToName.Keys.OrderBy(c => c, StringComparer.Ordinal).ToList();
        if (string.IsNullOrWhiteSpace(keyword)) return all;

        var k = keyword.Trim();
        return all
            .Where(c => c.StartsWith(k, StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .ToList();
    }
}
