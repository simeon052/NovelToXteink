using System;
using System.Text.RegularExpressions;

namespace NovelToEink.Core;

/// <summary>タイトルから【書籍発売中】類のマーケティングタグ括弧を除去する。</summary>
public static partial class TitleCleaner
{
    // 性能レビュー対応: Coreの他正規表現と同じく [GeneratedRegex]（source generatorでコンパイル時生成）。
    [GeneratedRegex(
        $@"(?:【{B}*?{Kw}{B}*?】|〔{B}*?{Kw}{B}*?〕|\[{B}*?{Kw}{B}*?\])",
        RegexOptions.IgnoreCase)]
    private static partial Regex MarketingTag();
    // Kw は必ず (?: ) でグループ化（alternationが括弧ペアを外へ壊すのを防止）
    private const string Kw = "(?:発売|書籍化|コミカライズ|漫画化|マンガ化|アニメ化|ドラマ化|映画化|電子書籍|連載版)";
    // B は全括弧文字を排除 → 異種括弧の混成マッチ（例: [〜】）を防ぐ
    private const string B = "[^【〕〔\\[\\]]";

    public static string? Clean(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return title;
        return MarketingTag().Replace(title, "").Trim();
    }
}
