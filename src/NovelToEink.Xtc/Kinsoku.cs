namespace NovelToEink.Xtc;

/// <summary>
/// 日本語組版の禁則処理。行頭・行末に置いてはいけない文字を判定する。
/// </summary>
public static class Kinsoku
{
    // 行頭禁則：句読点・閉じ括弧・小書き仮名・長音・繰り返し記号など。
    private const string LineStartForbidden =
        "、。，．・：；？！ー―‐〜～ヽヾゝゞ々’”）〕］｝〉》」』】〙〗»" +
        "ぁぃぅぇぉっゃゅょゎゕゖァィゥェォッャュョヮヵヶ" +
        "!?,.:;)]}｡｢｣､ﾞﾟ" +
        "︑︒︶︸﹀︺﹂﹄︼︾﹈";

    // 行末禁則：開き括弧の類。
    private const string LineEndForbidden =
        "‘“（〔［｛〈《「『【〘〖«" +
        "([{｛" +
        "︵︷︿︹﹁﹃︻︽﹇";

    /// <summary>行頭に置けない文字か（句読点・閉じ括弧・小書き仮名など）。</summary>
    public static bool IsForbiddenAtLineStart(char c) => LineStartForbidden.Contains(c);

    /// <summary>行末に置けない文字か（開き括弧など）。</summary>
    public static bool IsForbiddenAtLineEnd(char c) => LineEndForbidden.Contains(c);
}
