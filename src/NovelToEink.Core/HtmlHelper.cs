using HtmlAgilityPack;

namespace NovelToEink.Core;

/// <summary>
/// 共通 HTML / XPath ヘルパー。HtmlAgilityPack の select → first-match とテキスト抽出を一元化する。
/// </summary>
public static class HtmlHelper
{
    /// <summary>複数の XPath を上から順に試し、最初にマッチしたノードを返す。</summary>
    public static HtmlNode? FirstNode(HtmlNode root, params string[] xpaths)
    {
        foreach (var xp in xpaths)
        {
            var n = root.SelectSingleNode(xp);
            if (n != null) return n;
        }
        return null;
    }

    /// <summary>複数の XPath を上から順に試し、最初にマッチしたノードのテキストを返す。</summary>
    public static string? TextOf(HtmlNode root, params string[] xpaths)
    {
        var n = FirstNode(root, xpaths);
        if (n == null) return null;
        var t = HtmlEntity.DeEntitize(n.InnerText);
        return string.IsNullOrWhiteSpace(t) ? null : t.Trim();
    }
}
