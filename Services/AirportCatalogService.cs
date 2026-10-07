using System.Collections.Generic;
using System.Linq;
using AirPhotoGarage.Models;

namespace AirPhotoGarage.Services;

public interface IAirportCatalogService
{
    /// <summary>用 IATA 三字代码查机场。</summary>
    Airport? FindByIata(string iata);

    /// <summary>用 ICAO 四字代码查机场。</summary>
    Airport? FindByIcao(string icao);

    /// <summary>用名称关键字查机场（子串匹配，不区分大小写）。</summary>
    IReadOnlyList<Airport> SearchByName(string keyword);

    /// <summary>所有机场列表（按 IATA 排序）。</summary>
    IReadOnlyList<Airport> All { get; }
}

/// <summary>
/// 内置常见机场目录。覆盖中国主要机场 + 国际主要枢纽。
/// 后续可扩展为从 OpenFlights / OurAirports 导入完整数据集。
/// </summary>
public sealed class AirportCatalogService : IAirportCatalogService
{
    private readonly Dictionary<string, Airport> _byIata = new(System.StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Airport> _byIcao = new(System.StringComparer.OrdinalIgnoreCase);

    public AirportCatalogService()
    {
        foreach (var a in SeedAirports)
        {
            if (!string.IsNullOrWhiteSpace(a.Iata))
                _byIata[a.Iata] = a;
            if (!string.IsNullOrWhiteSpace(a.Icao))
                _byIcao[a.Icao] = a;
        }
    }

    public IReadOnlyList<Airport> All => _byIata.Values
        .OrderBy(a => a.Iata)
        .ToList();

    public Airport? FindByIata(string iata)
    {
        if (string.IsNullOrWhiteSpace(iata)) return null;
        _byIata.TryGetValue(iata.Trim(), out var a);
        return a;
    }

    public Airport? FindByIcao(string icao)
    {
        if (string.IsNullOrWhiteSpace(icao)) return null;
        _byIcao.TryGetValue(icao.Trim(), out var a);
        return a;
    }

    public IReadOnlyList<Airport> SearchByName(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return All;
        var k = keyword.Trim();
        return _byIata.Values
            .Where(a => a.Name.Contains(k, System.StringComparison.OrdinalIgnoreCase)
                     || (a.City?.Contains(k, System.StringComparison.OrdinalIgnoreCase) ?? false)
                     || (a.Iata?.Contains(k, System.StringComparison.OrdinalIgnoreCase) ?? false)
                     || (a.Icao?.Contains(k, System.StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(a => a.Name)
            .ToList();
    }

    // ---- 种子数据 ----

    private static readonly Airport[] SeedAirports =
    {
        // ---- 中国大陆 ----
        new() { Iata = "PEK", Icao = "ZBAA", Name = "北京首都国际机场", City = "北京", Country = "CN", Latitude = 40.0801, Longitude = 116.5846 },
        new() { Iata = "PKX", Icao = "ZBAD", Name = "北京大兴国际机场", City = "北京", Country = "CN", Latitude = 39.5098, Longitude = 116.4109 },
        new() { Iata = "PVG", Icao = "ZSPD", Name = "上海浦东国际机场", City = "上海", Country = "CN", Latitude = 31.1434, Longitude = 121.8052 },
        new() { Iata = "SHA", Icao = "ZSSS", Name = "上海虹桥国际机场", City = "上海", Country = "CN", Latitude = 31.1979, Longitude = 121.3363 },
        new() { Iata = "CAN", Icao = "ZGGG", Name = "广州白云国际机场", City = "广州", Country = "CN", Latitude = 23.3924, Longitude = 113.2988 },
        new() { Iata = "SZX", Icao = "ZGSZ", Name = "深圳宝安国际机场", City = "深圳", Country = "CN", Latitude = 22.6393, Longitude = 113.8108 },
        new() { Iata = "CTU", Icao = "ZUUU", Name = "成都双流国际机场", City = "成都", Country = "CN", Latitude = 30.5785, Longitude = 103.9471 },
        new() { Iata = "TFU", Icao = "ZUTF", Name = "成都天府国际机场", City = "成都", Country = "CN", Latitude = 30.3125, Longitude = 104.4419 },
        new() { Iata = "KMG", Icao = "ZPPP", Name = "昆明长水国际机场", City = "昆明", Country = "CN", Latitude = 25.1019, Longitude = 102.9292 },
        new() { Iata = "HGH", Icao = "ZSHC", Name = "杭州萧山国际机场", City = "杭州", Country = "CN", Latitude = 30.2295, Longitude = 120.4347 },
        new() { Iata = "NKG", Icao = "ZSNJ", Name = "南京禄口国际机场", City = "南京", Country = "CN", Latitude = 31.7420, Longitude = 118.8620 },
        new() { Iata = "KWE", Icao = "ZUGY", Name = "贵阳龙洞堡国际机场", City = "贵阳", Country = "CN", Latitude = 26.5385, Longitude = 106.8011 },
        new() { Iata = "CKG", Icao = "ZUCK", Name = "重庆江北国际机场", City = "重庆", Country = "CN", Latitude = 29.7192, Longitude = 106.6418 },
        new() { Iata = "WUH", Icao = "ZHHH", Name = "武汉天河国际机场", City = "武汉", Country = "CN", Latitude = 30.7838, Longitude = 114.2081 },
        new() { Iata = "SJW", Icao = "ZBSJ", Name = "石家庄正定国际机场", City = "石家庄", Country = "CN", Latitude = 38.2806, Longitude = 114.6973 },
        new() { Iata = "SHE", Icao = "ZYTX", Name = "沈阳桃仙国际机场", City = "沈阳", Country = "CN", Latitude = 41.6398, Longitude = 123.4836 },
        new() { Iata = "HRB", Icao = "ZYHB", Name = "哈尔滨太平国际机场", City = "哈尔滨", Country = "CN", Latitude = 45.6234, Longitude = 126.2502 },
        new() { Iata = "XMN", Icao = "ZSAM", Name = "厦门高崎国际机场", City = "厦门", Country = "CN", Latitude = 24.5440, Longitude = 118.1276 },
        new() { Iata = "SYX", Icao = "ZJSY", Name = "三亚凤凰国际机场", City = "三亚", Country = "CN", Latitude = 18.3029, Longitude = 109.4124 },
        new() { Iata = "HAK", Icao = "ZJHK", Name = "海口美兰国际机场", City = "海口", Country = "CN", Latitude = 19.9349, Longitude = 110.4589 },
        new() { Iata = "DLC", Icao = "ZYTL", Name = "大连周水子国际机场", City = "大连", Country = "CN", Latitude = 38.9657, Longitude = 121.5386 },
        new() { Iata = "TSN", Icao = "ZBTJ", Name = "天津滨海国际机场", City = "天津", Country = "CN", Latitude = 39.1244, Longitude = 117.3464 },
        new() { Iata = "FOC", Icao = "ZSFZ", Name = "福州长乐国际机场", City = "福州", Country = "CN", Latitude = 25.9351, Longitude = 119.6633 },
        new() { Iata = "HFE", Icao = "ZSOF", Name = "合肥新桥国际机场", City = "合肥", Country = "CN", Latitude = 31.9799, Longitude = 116.9789 },
        new() { Iata = "CGO", Icao = "ZHCC", Name = "郑州新郑国际机场", City = "郑州", Country = "CN", Latitude = 34.5197, Longitude = 113.8408 },
        new() { Iata = "XIY", Icao = "ZLXY", Name = "西安咸阳国际机场", City = "西安", Country = "CN", Latitude = 34.4471, Longitude = 108.7516 },
        new() { Iata = "LJG", Icao = "ZPLJ", Name = "丽江三义国际机场", City = "丽江", Country = "CN", Latitude = 26.6800, Longitude = 100.2461 },
        new() { Iata = "LXA", Icao = "ZULS", Name = "拉萨贡嘎国际机场", City = "拉萨", Country = "CN", Latitude = 29.2978, Longitude = 90.9113 },
        new() { Iata = "UYN", Icao = "ZLYL", Name = "榆林榆阳国际机场", City = "榆林", Country = "CN", Latitude = 38.3210, Longitude = 109.7910 },
        new() { Iata = "LZO", Icao = "ZULZ", Name = "泸州云龙机场", City = "泸州", Country = "CN", Latitude = 29.0300, Longitude = 105.4700 },
        new() { Iata = "CGD", Icao = "ZGCD", Name = "常德桃花源机场", City = "常德", Country = "CN", Latitude = 29.0731, Longitude = 111.6410 },

        // ---- 中国港澳台 ----
        new() { Iata = "HKG", Icao = "VHHH", Name = "香港国际机场", City = "香港", Country = "HK", Latitude = 22.3080, Longitude = 113.9185 },
        new() { Iata = "MFM", Icao = "VMMC", Name = "澳门国际机场", City = "澳门", Country = "MO", Latitude = 22.1490, Longitude = 113.5915 },
        new() { Iata = "TPE", Icao = "RCTP", Name = "台湾桃园国际机场", City = "台北", Country = "TW", Latitude = 25.0797, Longitude = 121.2342 },
        new() { Iata = "TSA", Icao = "RCSS", Name = "台北松山国际机场", City = "台北", Country = "TW", Latitude = 25.0797, Longitude = 121.5500 },
        new() { Iata = "KHH", Icao = "RCKH", Name = "高雄国际机场", City = "高雄", Country = "TW", Latitude = 22.5771, Longitude = 120.3501 },

        // ---- 日本 / 韩国 ----
        new() { Iata = "HND", Icao = "RJTT", Name = "东京羽田机场", City = "东京", Country = "JP", Latitude = 35.5494, Longitude = 139.7798 },
        new() { Iata = "NRT", Icao = "RJAA", Name = "成田国际机场", City = "东京", Country = "JP", Latitude = 35.7647, Longitude = 140.3863 },
        new() { Iata = "KIX", Icao = "RJBB", Name = "关西国际机场", City = "大阪", Country = "JP", Latitude = 34.4347, Longitude = 135.2440 },
        new() { Iata = "NGO", Icao = "RJGG", Name = "中部国际机场", City = "名古屋", Country = "JP", Latitude = 34.8580, Longitude = 136.8054 },
        new() { Iata = "FUK", Icao = "RJFF", Name = "福冈机场", City = "福冈", Country = "JP", Latitude = 33.5859, Longitude = 130.4507 },
        new() { Iata = "CTS", Icao = "RJCC", Name = "新千岁机场", City = "札幌", Country = "JP", Latitude = 42.7752, Longitude = 141.6923 },
        new() { Iata = "OKA", Icao = "ROAH", Name = "那霸机场", City = "冲绳", Country = "JP", Latitude = 26.1958, Longitude = 127.6459 },
        new() { Iata = "ICN", Icao = "RKSI", Name = "仁川国际机场", City = "首尔", Country = "KR", Latitude = 37.4602, Longitude = 126.4407 },
        new() { Iata = "GMP", Icao = "RKSS", Name = "金浦国际机场", City = "首尔", Country = "KR", Latitude = 37.5586, Longitude = 126.7900 },
        new() { Iata = "PUS", Icao = "RKPK", Name = "金海国际机场", City = "釜山", Country = "KR", Latitude = 35.1795, Longitude = 128.9382 },

        // ---- 东南亚 ----
        new() { Iata = "SIN", Icao = "WSSS", Name = "新加坡樟宜机场", City = "新加坡", Country = "SG", Latitude = 1.3644, Longitude = 103.9915 },
        new() { Iata = "KUL", Icao = "WMKK", Name = "吉隆坡国际机场", City = "吉隆坡", Country = "MY", Latitude = 2.7456, Longitude = 101.7099 },
        new() { Iata = "BKK", Icao = "VTBS", Name = "素万那普国际机场", City = "曼谷", Country = "TH", Latitude = 13.6900, Longitude = 100.7501 },
        new() { Iata = "DMK", Icao = "VTBD", Name = "廊曼国际机场", City = "曼谷", Country = "TH", Latitude = 13.9126, Longitude = 100.6068 },
        new() { Iata = "MNL", Icao = "RPLL", Name = "尼诺伊·阿基诺国际机场", City = "马尼拉", Country = "PH", Latitude = 14.5086, Longitude = 121.0194 },
        new() { Iata = "CGK", Icao = "WIII", Name = "苏加诺-哈达国际机场", City = "雅加达", Country = "ID", Latitude = -6.1256, Longitude = 106.6559 },
        new() { Iata = "HAN", Icao = "VVNB", Name = "内排国际机场", City = "河内", Country = "VN", Latitude = 21.2212, Longitude = 105.8070 },
        new() { Iata = "SGN", Icao = "VVTS", Name = "新山一国际机场", City = "胡志明市", Country = "VN", Latitude = 10.8188, Longitude = 106.6520 },

        // ---- 南亚 / 中东 ----
        new() { Iata = "DEL", Icao = "VIDP", Name = "英迪拉·甘地国际机场", City = "新德里", Country = "IN", Latitude = 28.5562, Longitude = 77.1000 },
        new() { Iata = "BOM", Icao = "VABB", Name = "贾特拉帕蒂·希瓦吉国际机场", City = "孟买", Country = "IN", Latitude = 19.0896, Longitude = 72.8656 },
        new() { Iata = "DXB", Icao = "OMDB", Name = "迪拜国际机场", City = "迪拜", Country = "AE", Latitude = 25.2532, Longitude = 55.3657 },
        new() { Iata = "DOH", Icao = "OTHH", Name = "哈马德国际机场", City = "多哈", Country = "QA", Latitude = 25.2731, Longitude = 51.6080 },
        new() { Iata = "IST", Icao = "LTFM", Name = "伊斯坦布尔机场", City = "伊斯坦布尔", Country = "TR", Latitude = 41.2753, Longitude = 28.7519 },

        // ---- 美洲 ----
        new() { Iata = "JFK", Icao = "KJFK", Name = "约翰·F·肯尼迪国际机场", City = "纽约", Country = "US", Latitude = 40.6413, Longitude = -73.7781 },
        new() { Iata = "LAX", Icao = "KLAX", Name = "洛杉矶国际机场", City = "洛杉矶", Country = "US", Latitude = 33.9416, Longitude = -118.4085 },
        new() { Iata = "SFO", Icao = "KSFO", Name = "旧金山国际机场", City = "旧金山", Country = "US", Latitude = 37.6213, Longitude = -122.3790 },
        new() { Iata = "SEA", Icao = "KSEA", Name = "西雅图-塔科马国际机场", City = "西雅图", Country = "US", Latitude = 47.4502, Longitude = -122.3088 },
        new() { Iata = "ORD", Icao = "KORD", Name = "奥黑尔国际机场", City = "芝加哥", Country = "US", Latitude = 41.9742, Longitude = -87.9073 },
        new() { Iata = "ATL", Icao = "KATL", Name = "哈兹菲尔德-杰克逊国际机场", City = "亚特兰大", Country = "US", Latitude = 33.6407, Longitude = -84.4277 },
        new() { Iata = "DFW", Icao = "KDFW", Name = "达拉斯-沃斯堡国际机场", City = "达拉斯", Country = "US", Latitude = 32.8998, Longitude = -97.0403 },
        new() { Iata = "DEN", Icao = "KDEN", Name = "丹佛国际机场", City = "丹佛", Country = "US", Latitude = 39.8561, Longitude = -104.6737 },
        new() { Iata = "YYZ", Icao = "CYYZ", Name = "多伦多皮尔逊国际机场", City = "多伦多", Country = "CA", Latitude = 43.6777, Longitude = -79.6248 },
        new() { Iata = "YVR", Icao = "CYVR", Name = "温哥华国际机场", City = "温哥华", Country = "CA", Latitude = 49.1947, Longitude = -123.1790 },
        new() { Iata = "GRU", Icao = "SBGR", Name = "圣保罗瓜鲁柳斯国际机场", City = "圣保罗", Country = "BR", Latitude = -23.4356, Longitude = -46.4731 },

        // ---- 欧洲 ----
        new() { Iata = "LHR", Icao = "EGLL", Name = "伦敦希思罗机场", City = "伦敦", Country = "GB", Latitude = 51.4700, Longitude = -0.4543 },
        new() { Iata = "LGW", Icao = "EGKK", Name = "伦敦盖特威克机场", City = "伦敦", Country = "GB", Latitude = 51.1537, Longitude = -0.1821 },
        new() { Iata = "CDG", Icao = "LFPG", Name = "巴黎戴高乐机场", City = "巴黎", Country = "FR", Latitude = 49.0097, Longitude = 2.5479 },
        new() { Iata = "ORY", Icao = "LFPO", Name = "巴黎奥利机场", City = "巴黎", Country = "FR", Latitude = 48.7233, Longitude = 2.3794 },
        new() { Iata = "FRA", Icao = "EDDF", Name = "法兰克福机场", City = "法兰克福", Country = "DE", Latitude = 50.0379, Longitude = 8.5622 },
        new() { Iata = "MUC", Icao = "EDDM", Name = "慕尼黑机场", City = "慕尼黑", Country = "DE", Latitude = 48.3538, Longitude = 11.7861 },
        new() { Iata = "AMS", Icao = "EHAM", Name = "阿姆斯特丹史基浦机场", City = "阿姆斯特丹", Country = "NL", Latitude = 52.3105, Longitude = 4.7683 },
        new() { Iata = "BRU", Icao = "EBBR", Name = "布鲁塞尔机场", City = "布鲁塞尔", Country = "BE", Latitude = 50.9014, Longitude = 4.4844 },
        new() { Iata = "ZRH", Icao = "LSZH", Name = "苏黎世机场", City = "苏黎世", Country = "CH", Latitude = 47.4647, Longitude = 8.5492 },
        new() { Iata = "VIE", Icao = "LOWW", Name = "维也纳国际机场", City = "维也纳", Country = "AT", Latitude = 48.1102, Longitude = 16.5697 },
        new() { Iata = "MAD", Icao = "LEMD", Name = "马德里巴拉哈斯机场", City = "马德里", Country = "ES", Latitude = 40.4983, Longitude = -3.5676 },
        new() { Iata = "BCN", Icao = "LEBL", Name = "巴塞罗那机场", City = "巴塞罗那", Country = "ES", Latitude = 41.2974, Longitude = 2.0833 },
        new() { Iata = "FCO", Icao = "LIRF", Name = "罗马菲乌米奇诺机场", City = "罗马", Country = "IT", Latitude = 41.8003, Longitude = 12.2389 },
        new() { Iata = "MXP", Icao = "LIMC", Name = "米兰马尔彭萨机场", City = "米兰", Country = "IT", Latitude = 45.6306, Longitude = 8.7281 },
        new() { Iata = "CPH", Icao = "EKCH", Name = "哥本哈根机场", City = "哥本哈根", Country = "DK", Latitude = 55.6180, Longitude = 12.6508 },
        new() { Iata = "ARN", Icao = "ESSA", Name = "斯德哥尔摩阿兰达机场", City = "斯德哥尔摩", Country = "SE", Latitude = 59.6519, Longitude = 17.9186 },
        new() { Iata = "OSL", Icao = "ENGM", Name = "奥斯陆加勒穆恩机场", City = "奥斯陆", Country = "NO", Latitude = 60.1976, Longitude = 11.1004 },
        new() { Iata = "HEL", Icao = "EFHK", Name = "赫尔辛基机场", City = "赫尔辛基", Country = "FI", Latitude = 60.3172, Longitude = 24.9633 },
        new() { Iata = "SVO", Icao = "UUEE", Name = "莫斯科谢列梅捷沃机场", City = "莫斯科", Country = "RU", Latitude = 55.9726, Longitude = 37.4146 },
        new() { Iata = "DME", Icao = "UUDD", Name = "莫斯科多莫杰多沃机场", City = "莫斯科", Country = "RU", Latitude = 55.4088, Longitude = 37.9063 },

        // ---- 大洋洲 ----
        new() { Iata = "SYD", Icao = "YSSY", Name = "悉尼金斯福德·史密斯机场", City = "悉尼", Country = "AU", Latitude = -33.9399, Longitude = 151.1753 },
        new() { Iata = "MEL", Icao = "YMML", Name = "墨尔本机场", City = "墨尔本", Country = "AU", Latitude = -37.6733, Longitude = 144.8430 },
        new() { Iata = "AKL", Icao = "NZAA", Name = "奥克兰机场", City = "奥克兰", Country = "NZ", Latitude = -37.0082, Longitude = 174.7920 },

        // ---- 非洲 ----
        new() { Iata = "JNB", Icao = "FAOR", Name = "约翰内斯堡奥·坦博国际机场", City = "约翰内斯堡", Country = "ZA", Latitude = -26.1392, Longitude = 28.2460 },
        new() { Iata = "CAI", Icao = "HECA", Name = "开罗国际机场", City = "开罗", Country = "EG", Latitude = 30.1219, Longitude = 31.4056 },
    };
}
