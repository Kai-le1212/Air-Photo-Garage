using System.Collections.Generic;
using System.Linq;

namespace AirPhotoGarage.Services;

public interface IAircraftCatalogService
{
    /// <summary>所有机型（按 manufacturer → model 排序）。</summary>
    IReadOnlyList<string> All { get; }

    /// <summary>
    /// 按关键字搜索（不区分大小写，匹配 manufacturer 或 model）。
    /// <b>前缀命中的排在前面</b>，其余「中间包含」的排在后面 ——
    /// 这样输入「B」先出 Boeing，而不是被 "Airbus ..." 挤掉。
    /// </summary>
    IReadOnlyList<string> Search(string keyword);
}

/// <summary>
/// 内置常见飞机型号目录（~150 种主流民航 + 公务机）。
/// 后续可替换为在线服务（planespotters.net / hexdb.io 等）。
/// </summary>
public sealed class AircraftCatalogService : IAircraftCatalogService
{
    private readonly List<string> _models;

    public AircraftCatalogService()
    {
        _models = SeedModels.OrderBy(m => m).ToList();
    }

    public IReadOnlyList<string> All => _models;

    /// <summary>
    /// 按关键字搜索（不区分大小写）。<b>排序规则</b>（用户指定的口径）：
    ///
    /// <list type="number">
    /// <item><b>完全符合</b>：型号段与输入一模一样（输入 <c>A320</c> / <c>737-800</c>）排最前；</item>
    /// <item><b>品牌名开头</b>：输入字母时，品牌第一个字母对上的排在前
    ///   （<c>B</c> → Boeing / Bombardier，<c>A</c> → Airbus / ATR …）；</item>
    /// <item><b>型号段开头</b>：输入数字时（<c>737</c>）型号段以它开头的排在前；</item>
    /// <item>其余「中间包含」的排最后。</item>
    /// </list>
    ///
    /// 同一层内保持字母序（<see cref="_models"/> 已按字母排好，<c>OrderBy</c> 是稳定排序），
    /// 所以同一品牌会自然聚在一起。
    /// </summary>
    public IReadOnlyList<string> Search(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return All;
        var k = keyword.Trim();

        return _models
            .Where(m => m.Contains(k, System.StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => AircraftModelRanking.Rank(m, k))
            .Take(20)
            .ToList();
    }

    /// <summary>
    /// 排序层级（越小越靠前）。规则集中在 <see cref="AircraftModelRanking"/>，
    /// 与照片墙筛选面板共用同一份，避免两处排序不一致。
    /// </summary>
    private static int Rank(string model, string keyword) => AircraftModelRanking.Rank(model, keyword);

    private static readonly string[] SeedModels =
    {
        // ---- Boeing ----
        "Boeing 737-700",
        "Boeing 737-800",
        "Boeing 737-900",
        "Boeing 737 MAX 8",
        "Boeing 737 MAX 9",
        "Boeing 737 MAX 10",
        "Boeing 747-400",
        "Boeing 747-8",
        "Boeing 757-200",
        "Boeing 757-300",
        "Boeing 767-200",
        "Boeing 767-300",
        "Boeing 767-400ER",
        "Boeing 777-200",
        "Boeing 777-200ER",
        "Boeing 777-300",
        "Boeing 777-300ER",
        "Boeing 777-9",
        "Boeing 787-8",
        "Boeing 787-9",
        "Boeing 787-10",

        // ---- Airbus ----
        "Airbus A220-100",
        "Airbus A220-300",
        "Airbus A318",
        "Airbus A319",
        "Airbus A319neo",
        "Airbus A320",
        "Airbus A320neo",
        "Airbus A321",
        "Airbus A321neo",
        "Airbus A330-200",
        "Airbus A330-300",
        "Airbus A330-700L Beluga",
        "Airbus A330-900neo",
        "Airbus A340-300",
        "Airbus A340-500",
        "Airbus A340-600",
        "Airbus A350-900",
        "Airbus A350-1000",
        "Airbus A380-800",

        // ---- COMAC / 中国 ----
        "COMAC ARJ21-700",
        "COMAC C919",
        "COMAC C929",
        "XAC Y-8",
        "XAC Y-12",
        "AVIC AG600",

        // ---- Embraer ----
        "Embraer E170",
        "Embraer E175",
        "Embraer E190",
        "Embraer E195",
        "Embraer E190-E2",
        "Embraer E195-E2",
        "Embraer ERJ-135",
        "Embraer ERJ-140",
        "Embraer ERJ-145",
        "Embraer Phenom 100",
        "Embraer Phenom 300",
        "Embraer Legacy 600",
        "Embraer Legacy 650",

        // ---- Bombardier / De Havilland Canada ----
        "Bombardier CRJ-200",
        "Bombardier CRJ-700",
        "Bombardier CRJ-900",
        "Bombardier CRJ-1000",
        "Bombardier Dash 8 Q200",
        "Bombardier Dash 8 Q300",
        "Bombardier Dash 8 Q400",
        "Bombardier Global 6000",
        "Bombardier Global 7500",
        "Bombardier Learjet 75",

        // ---- Cessna ----
        "Cessna 172 Skyhawk",
        "Cessna 182 Skylane",
        "Cessna 208 Caravan",
        "Cessna 208B Grand Caravan",
        "Cessna 525 CitationJet",
        "Cessna 560 Citation XLS",
        "Cessna 680 Citation Sovereign",
        "Cessna 750 Citation X",

        // ---- Gulfstream ----
        "Gulfstream G450",
        "Gulfstream G550",
        "Gulfstream G650",
        "Gulfstream G700",
        "Gulfstream G800",

        // ---- Dassault ----
        "Dassault Falcon 7X",
        "Dassault Falcon 8X",
        "Dassault Falcon 2000",
        "Dassault Falcon 900",

        // ---- ATR ----
        "ATR 42-300",
        "ATR 42-500",
        "ATR 72-200",
        "ATR 72-500",
        "ATR 72-600",

        // ---- McDonnell Douglas (历史机型) ----
        "McDonnell Douglas MD-80",
        "McDonnell Douglas MD-82",
        "McDonnell Douglas MD-83",
        "McDonnell Douglas MD-90",
        "McDonnell Douglas DC-10",
        "McDonnell Douglas MD-11",

        // ---- Lockheed (历史机型) ----
        "Lockheed L-1011 TriStar",
        "Lockheed C-130 Hercules",
        "Lockheed Martin F-16",
        "Lockheed Martin F-22",

        // ---- 俄罗斯 / 苏联 ----
        "Antonov An-12",
        "Antonov An-24",
        "Antonov An-26",
        "Antonov An-124 Ruslan",
        "Antonov An-225 Mriya",
        "Ilyushin Il-62",
        "Ilyushin Il-76",
        "Ilyushin Il-86",
        "Ilyushin Il-96",
        "Ilyushin Il-114",
        "Tupolev Tu-134",
        "Tupolev Tu-154",
        "Tupolev Tu-204",
        "Tupolev Tu-214",
        "Tupolev Tu-334",
        "Yakovlev Yak-40",
        "Yakovlev Yak-42",

        // ---- Irkut / Sukhoi ----
        "Irkut MC-21",
        "Sukhoi Superjet 100",

        // ---- 军用 / 特殊 ----
        "Airbus A400M Atlas",
        "Boeing C-17 Globemaster III",
        "Boeing KC-46 Pegasus",
        "Airbus KC-330 MRTT",
        "Boeing E-7 Wedgetail",
        "Boeing P-8 Poseidon",
    };
}
