using System.Text;
using HtmlAgilityPack;

namespace NovelToEink.Core;

/// <summary>
/// 小説サイトの本文ノードを、軽量で整形式な XHTML 断片へ変換する。
/// 余計な属性・script・style を排除し、&lt;p&gt;/&lt;br&gt;/&lt;ruby&gt;/&lt;img&gt; など
/// 表示に必要な最小限のタグだけを残す。
/// </summary>
public static class XhtmlSanitizer
{
    /// <summary>本文コンテナを変換する。検出した画像URL（絶対URL）も返す。</summary>
    public static (string Xhtml, List<string> ImageUrls) Clean(
        HtmlNode container, string baseUrl, EpubOptions options)
    {
        var images = new List<string>();
        var sb = new StringBuilder();
        var inline = new StringBuilder();

        void FlushInline()
        {
            if (inline.Length == 0) return;
            var s = inline.ToString();
            inline.Clear();
            if (s.Trim().Length == 0) return;
            sb.Append("<p>").Append(s).Append("</p>\n");
        }

        foreach (var child in container.ChildNodes)
        {
            if (IsBlock(child))
            {
                FlushInline();
                var inner = SerializeInline(child, baseUrl, options, images);
                if (inner.Trim().Length == 0)
                    sb.Append("<p><br/></p>\n"); // 空行を維持
                else
                    sb.Append("<p>").Append(inner).Append("</p>\n");
            }
            else
            {
                inline.Append(SerializeInline(child, baseUrl, options, images));
            }
        }
        FlushInline();
        return (sb.ToString(), images);
    }

    private static bool IsBlock(HtmlNode node)
        => node.NodeType == HtmlNodeType.Element &&
           (node.Name is "p" or "div");

    private static string SerializeInline(HtmlNode node, string baseUrl, EpubOptions opt, List<string> images)
    {
        switch (node.NodeType)
        {
            case HtmlNodeType.Text:
                return XmlEscape(HtmlEntity.DeEntitize(node.InnerText));
            case HtmlNodeType.Comment:
                return "";
        }

        var name = node.Name.ToLowerInvariant();
        switch (name)
        {
            case "br":
                return "<br/>";

            case "script":
            case "style":
            case "noscript":
                return "";

            case "ruby":
                if (!opt.KeepRuby)
                    return ChildrenInline(node, baseUrl, opt, images); // rt/rp は下で除外される
                return "<ruby>" + ChildrenInline(node, baseUrl, opt, images) + "</ruby>";

            case "rb":
                // 親文字。ルビ有無に関わらず本文として残す。
                return ChildrenInline(node, baseUrl, opt, images);

            case "rt":
                return opt.KeepRuby ? "<rt>" + ChildrenInline(node, baseUrl, opt, images) + "</rt>" : "";

            case "rp":
                return opt.KeepRuby ? "<rp>" + ChildrenInline(node, baseUrl, opt, images) + "</rp>" : "";

            case "b":
            case "strong":
                return "<strong>" + ChildrenInline(node, baseUrl, opt, images) + "</strong>";

            case "i":
            case "em":
                return "<em>" + ChildrenInline(node, baseUrl, opt, images) + "</em>";

            case "img":
                if (!opt.IncludeInlineImages) return "";
                var src = node.GetAttributeValue("src", "");
                if (string.IsNullOrWhiteSpace(src)) return "";
                var abs = ResolveUrl(baseUrl, src);
                if (abs is null) return "";
                if (!images.Contains(abs)) images.Add(abs);
                var alt = XmlEscape(HtmlEntity.DeEntitize(node.GetAttributeValue("alt", "")));
                return $"<img src=\"{XmlEscape(abs)}\" alt=\"{alt}\"/>";

            case "a":
                // リンクは外し、中身（多くは挿絵の img）だけ残す。
                return ChildrenInline(node, baseUrl, opt, images);

            default:
                // span など未知のインライン要素は中身だけ残す。
                return ChildrenInline(node, baseUrl, opt, images);
        }
    }

    private static string ChildrenInline(HtmlNode node, string baseUrl, EpubOptions opt, List<string> images)
    {
        var sb = new StringBuilder();
        foreach (var c in node.ChildNodes)
            sb.Append(SerializeInline(c, baseUrl, opt, images));
        return sb.ToString();
    }

    /// <summary>相対/プロトコル相対URLを絶対URLへ。</summary>
    public static string? ResolveUrl(string baseUrl, string href)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;
        href = href.Trim();
        if (href.StartsWith("//")) return "https:" + href;
        if (Uri.TryCreate(new Uri(baseUrl), href, out var abs))
            return abs.ToString();
        return null;
    }

    public static string XmlEscape(string s)
        => s.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
}
