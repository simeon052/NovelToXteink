using HtmlAgilityPack;
using NovelToEink.Core;

namespace NovelToEink.Tests;

public class XhtmlSanitizerTests
{
    private const string Base = "https://ncode.syosetu.com/n1/1/";

    private static (string Xhtml, List<string> Images) Clean(string html, EpubOptions? options = null)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml($"<div id=\"c\">{html}</div>");
        return XhtmlSanitizer.Clean(doc.GetElementbyId("c"), Base, options ?? new EpubOptions());
    }

    [Fact]
    public void Paragraphs_BecomeXhtmlParagraphs()
    {
        var (xhtml, _) = Clean("<p>一行目</p><p>二行目</p>");
        Assert.Equal("<p>一行目</p>\n<p>二行目</p>\n", xhtml);
    }

    [Fact]
    public void EmptyParagraph_IsKeptAsABlankLine()
    {
        // 小説の空行は意味のある演出なので、<p><br/></p> として残す
        var (xhtml, _) = Clean("<p>前</p><p></p><p>後</p>");
        Assert.Contains("<p><br/></p>", xhtml);
        Assert.Equal(3, xhtml.Split("<p>").Length - 1);
    }

    [Fact]
    public void SpecialCharacters_AreEscaped_SoTheResultIsWellFormedXml()
    {
        var (xhtml, _) = Clean("<p>1 &lt; 2 &amp; 3 &gt; 2</p>");
        Assert.Contains("1 &lt; 2 &amp; 3 &gt; 2", xhtml);
        System.Xml.Linq.XDocument.Parse("<r>" + xhtml + "</r>");   // 例外が出なければ整形式
    }

    [Fact]
    public void ScriptAndStyle_AreDropped()
    {
        var (xhtml, _) = Clean("<p>本文</p><script>alert(1)</script><style>p{color:red}</style>");
        Assert.DoesNotContain("alert", xhtml);
        Assert.DoesNotContain("color:red", xhtml);
        Assert.Contains("本文", xhtml);
    }

    [Fact]
    public void Output_IsAlwaysWellFormedXml_EvenForSloppyHtml()
    {
        var (xhtml, _) = Clean("<p>閉じ忘れ<b>太字<p>次の段落<br>改行<i>斜体</p>");
        System.Xml.Linq.XDocument.Parse("<r>" + xhtml + "</r>");
    }

    [Theory]
    [InlineData("//cdn.example.com/a.png", "https://cdn.example.com/a.png")]   // プロトコル相対
    [InlineData("/img/a.png", "https://ncode.syosetu.com/img/a.png")]          // ルート相対
    [InlineData("a.png", "https://ncode.syosetu.com/n1/1/a.png")]              // 相対
    [InlineData("https://other.example/x.png", "https://other.example/x.png")] // 絶対
    public void ResolveUrl_MakesAbsoluteUrls(string href, string expected)
    {
        Assert.Equal(expected, XhtmlSanitizer.ResolveUrl(Base, href));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveUrl_BlankHref_ReturnsNull(string href)
    {
        Assert.Null(XhtmlSanitizer.ResolveUrl(Base, href));
    }

    [Theory]
    [InlineData("a&b", "a&amp;b")]
    [InlineData("<tag>", "&lt;tag&gt;")]
    [InlineData("say \"hi\"", "say &quot;hi&quot;")]
    [InlineData("&amp;", "&amp;amp;")]   // 二重エスケープ（入力が既にエスケープ済みでも、そのまま文字として扱う）
    public void XmlEscape_EscapesTheFiveXmlSpecials(string input, string expected)
    {
        Assert.Equal(expected, XhtmlSanitizer.XmlEscape(input));
    }
}
