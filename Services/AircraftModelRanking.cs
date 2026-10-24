namespace AirPhotoGarage.Services;

/// <summary>
/// 机型候选项的排序规则（用户指定的口径）—— <b>内置机型库与筛选面板共用这一份</b>，
/// 避免两处各写一套、出现「编辑对话框里 Boeing 在 Airbus 前、筛选面板里反过来」的漂移。
///
/// <para>层级（越小越靠前）：</para>
/// <list type="number">
/// <item><b>完全符合</b> —— 型号段与输入一模一样（输入 <c>A320</c>）；</item>
/// <item><b>品牌名开头</b> —— 输入字母时，品牌第一个字母对上（<c>B</c> → Boeing）；</item>
/// <item><b>型号段开头</b> —— 输入数字时（<c>737</c>），型号段以它开头；</item>
/// <item>其余「中间包含」。</item>
/// </list>
///
/// <para>
/// 第一个词算品牌、其余算型号段 —— 这样 "McDonnell Douglas MD-80" 的 <c>MD-80</c>
/// 也能被当作型号匹配到。
/// </para>
/// </summary>
public static class AircraftModelRanking
{
    /// <summary>返回排序层级；调用方按它做稳定排序（同层内保持原有字母序）。</summary>
    public static int Rank(string model, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return 0;

        var words = model.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return 3;

        var codeWords = words.Length > 1 ? words[1..] : words;

        // 型号段按「整段」比较（"737 MAX 10" 的型号段就是 "737 MAX 10"）。
        // 不能按单个词比：否则输入 737 时 "737 MAX 10" 会被当成「完全符合」插到 737-800 前面。
        var code = words.Length > 1 ? string.Join(' ', codeWords) : model;

        // 0) 完全符合
        if (string.Equals(model, keyword, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, keyword, System.StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        // 1) 品牌名开头（输入字母时的主排序）
        if (model.StartsWith(keyword, System.StringComparison.OrdinalIgnoreCase)) return 1;

        // 2) 型号段开头（输入数字时的主排序）；再退一步看型号段里的任一单词，
        //    多词品牌也能匹配到（"McDonnell Douglas MD-80" → MD）
        if (code.StartsWith(keyword, System.StringComparison.OrdinalIgnoreCase)) return 2;
        foreach (var w in codeWords)
        {
            if (w.StartsWith(keyword, System.StringComparison.OrdinalIgnoreCase)) return 2;
        }

        // 3) 其余中间包含
        return 3;
    }
}
