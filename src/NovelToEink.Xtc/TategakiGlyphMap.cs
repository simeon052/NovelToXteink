namespace NovelToEink.Xtc;

/// <summary>
/// 縦組み時に字形を差し替える文字のマッピング。
/// 括弧・句読点・リーダー類は Unicode の縦書き表示形（U+FE10〜U+FE4F 等）に置き換える。
/// </summary>
public static class TategakiGlyphMap
{
    private static readonly Dictionary<char, char> Map = new()
    {
        // リーダー・ダッシュ類
        ['…'] = '︙', ['‥'] = '︰',
        ['ー'] = '丨', ['─'] = '丨', ['―'] = '丨', ['－'] = '丨', ['-'] = '丨',
        ['～'] = '丨', ['〜'] = '丨', ['〰'] = '丨',

        // 括弧類
        ['「'] = '﹁', ['」'] = '﹂', ['『'] = '﹃', ['』'] = '﹄',
        ['（'] = '︵', ['）'] = '︶', ['('] = '︵', [')'] = '︶',
        ['【'] = '︻', ['】'] = '︼', ['〔'] = '︹', ['〕'] = '︺',
        ['［'] = '﹇', ['］'] = '﹈', ['['] = '﹇', [']'] = '﹈',
        ['｛'] = '︷', ['｝'] = '︸', ['{'] = '︷', ['}'] = '︸',
        ['＜'] = '︿', ['＞'] = '﹀', ['<'] = '︿', ['>'] = '﹀',
        ['《'] = '︽', ['》'] = '︾',

        // 句読点
        ['、'] = '︑', ['。'] = '︒',

        // 演算子・記号
        ['＝'] = '‖', ['='] = '‖',
        ['±'] = '∓', ['≠'] = '⧘', ['≒'] = '≓', ['≡'] = '⦀',
        ['：'] = '‥', [':'] = '‥',
    };

    /// <summary>縦組み用の字形があれば置き換えた文字を返す。無ければ元の文字をそのまま返す。</summary>
    public static char Translate(char c) => Map.TryGetValue(c, out var v) ? v : c;

    /// <summary>縦組み用の字形が定義されているか。</summary>
    public static bool HasVerticalForm(char c) => Map.ContainsKey(c);

    /// <summary>
    /// 縦組み用の字形をフォントが持たない場合に、元の文字を 90 度回転して描くべきか判定する。
    /// 括弧・ダッシュ類は回転で代替できるが、句読点は位置調整で対応するため回転しない。
    /// </summary>
    public static bool CanRotateAsFallback(char original) =>
        !"、。".Contains(original) && HasVerticalForm(original);
}
