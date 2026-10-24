using System;
using System.Collections.Generic;
using System.Linq;

namespace AirPhotoGarage.Services;

/// <summary>
/// 航空公司目录：<b>名称 ↔ 二字码</b> 双向补齐。
///
/// <para>
/// 用户只存两项（名称 + 二字码）：在「航空公司」框里填名称就自动带出二字码，
/// 填二字码就自动带出名称 —— 两边都不用手工抄一遍。
/// （三字码 / 呼号按用户要求<b>不存</b>。）
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
}

public sealed class AirlineCatalogService : IAirlineCatalogService
{
    /// <summary>（名称, 二字码）</summary>
    private static readonly (string Name, string Code)[] Seed =
    {
        // ---- 中国内地 ----
        ("中国国际航空", "CA"), ("中国东方航空", "MU"), ("中国南方航空", "CZ"),
        ("海南航空", "HU"), ("厦门航空", "MF"), ("深圳航空", "ZH"),
        ("四川航空", "3U"), ("山东航空", "SC"), ("春秋航空", "9C"),
        ("吉祥航空", "HO"), ("天津航空", "GS"), ("首都航空", "JD"),
        ("上海航空", "FM"), ("中国联合航空", "KN"), ("成都航空", "EU"),
        ("河北航空", "NS"), ("青岛航空", "QW"), ("长龙航空", "GJ"),
        ("西部航空", "PN"), ("奥凯航空", "BK"), ("华夏航空", "G5"),
        ("西藏航空", "TV"), ("昆明航空", "KY"), ("瑞丽航空", "DR"),
        ("东海航空", "DZ"), ("乌鲁木齐航空", "UQ"),
        ("顺丰航空", "O3"), ("中国邮政航空", "CF"), ("圆通航空", "YG"),

        // ---- 中国港澳台 ----
        ("国泰航空", "CX"), ("港龙航空", "KA"), ("香港航空", "HX"),
        ("香港快运", "UO"), ("澳门航空", "NX"),
        ("中华航空", "CI"), ("长荣航空", "BR"), ("星宇航空", "JX"),
        ("立荣航空", "B7"), ("华信航空", "AE"),

        // ---- 亚洲 ----
        ("新加坡航空", "SQ"), ("酷航", "TR"), ("马来西亚航空", "MH"),
        ("亚洲航空", "AK"), ("泰国国际航空", "TG"), ("泰越捷航空", "VZ"),
        ("越南航空", "VN"), ("越捷航空", "VJ"), ("菲律宾航空", "PR"),
        ("宿务太平洋航空", "5J"), ("印尼鹰航", "GA"), ("狮航", "JT"),
        ("全日空", "NH"), ("日本航空", "JL"), ("乐桃航空", "MM"),
        ("大韩航空", "KE"), ("韩亚航空", "OZ"), ("济州航空", "7C"),
        ("印度航空", "AI"), ("靛蓝航空", "6E"),

        // ---- 中东 / 土耳其 ----
        ("阿联酋航空", "EK"), ("阿提哈德航空", "EY"), ("卡塔尔航空", "QR"),
        ("土耳其航空", "TK"), ("沙特航空", "SV"), ("阿曼航空", "WY"),

        // ---- 欧洲 ----
        ("汉莎航空", "LH"), ("法国航空", "AF"), ("英国航空", "BA"),
        ("荷兰皇家航空", "KL"), ("瑞士国际航空", "LX"), ("奥地利航空", "OS"),
        ("北欧航空", "SK"), ("芬兰航空", "AY"), ("西班牙国家航空", "IB"),
        ("葡萄牙航空", "TP"), ("意大利航空", "AZ"), ("俄罗斯航空", "SU"),
        ("波兰航空", "LO"), ("爱尔兰航空", "EI"),

        // ---- 美洲 / 大洋洲 / 非洲 ----
        ("美国航空", "AA"), ("联合航空", "UA"), ("达美航空", "DL"),
        ("西南航空", "WN"), ("加拿大航空", "AC"), ("墨西哥航空", "AM"),
        ("澳洲航空", "QF"), ("维珍澳洲航空", "VA"), ("新西兰航空", "NZ"),
        ("埃塞俄比亚航空", "ET"), ("南非航空", "SA"), ("埃及航空", "MS"),

        // ---- 货运 ----
        ("联邦快递", "FX"), ("联合包裹", "5X"), ("极地航空货运", "PO"),
    };

    private readonly Dictionary<string, string> _byName;
    private readonly Dictionary<string, string> _byCode;
    private readonly List<string> _names;

    public AirlineCatalogService()
    {
        _byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _byCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, code) in Seed)
        {
            _byName[name] = code;
            _byCode[code] = name;
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
}
