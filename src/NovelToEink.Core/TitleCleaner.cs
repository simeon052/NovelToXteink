using System;
using System.Text.RegularExpressions;

namespace NovelToEink.Core;

/// <summary>タイトルから【書籍発売中】類のマーケティングタグ括弧を除去する。</summary>
public static class TitleCleaner
{
    private static readonly Regex MarketingTag = new(
        @"(?:【|〔|\[)[^〕\]】]*?(?:発売|書籍化|コミカライズ|漫画化|マンガ化|アニメ化|ドラマ化|映画化|電子書籍)[^〕\]】]*(?:〕|\]|】)");

    public static string? Clean(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return title;
        return MarketingTag.Replace(title, "").Trim();
    }
}
