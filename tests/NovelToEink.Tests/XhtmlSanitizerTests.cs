using System;
using Xunit;
using NovelToEink.Core;

namespace NovelToEink.Tests;

public class XhtmlSanitizerTests
{
    [Fact]
    public void XmlEscape_EscapesSpecialCharacters()
    {
        Assert.Equal("&lt;b&gt;", XhtmlSanitizer.XmlEscape("<b>"));
        Assert.Equal("&amp;", XhtmlSanitizer.XmlEscape("&"));
        Assert.Equal("&quot;", XhtmlSanitizer.XmlEscape("\""));
        Assert.Equal("a&gt;b", XhtmlSanitizer.XmlEscape("a>b"));
    }

    [Fact]
    public void XmlEscape_UnchangedWhenNoSpecialChars()
    {
        Assert.Equal("hello world", XhtmlSanitizer.XmlEscape("hello world"));
        Assert.Equal("", XhtmlSanitizer.XmlEscape(""));
    }

    [Fact]
    public void ResolveUrl_ResolvesRelativeUrl()
    {
        var url = XhtmlSanitizer.ResolveUrl("https://example.com/novel/chapter1.html", "./chapter2.html");
        Assert.Equal("https://example.com/novel/chapter2.html", url);
    }

    [Fact]
    public void ResolveUrl_ResolvesAbsoluteUrl()
    {
        var url = XhtmlSanitizer.ResolveUrl("https://example.com/novel/", "/other/page.html");
        Assert.Equal("https://example.com/other/page.html", url);
    }

    [Fact]
    public void ResolveUrl_KeepsHttpUrlAsIs()
    {
        var url = XhtmlSanitizer.ResolveUrl("https://example.com/", "https://other.com/img.png");
        Assert.Equal("https://other.com/img.png", url);
    }

    [Fact]
    public void ResolveUrl_HandlesAnchorUrls()
    {
        var url = XhtmlSanitizer.ResolveUrl("https://example.com/novel/chapter1.html", "#section1");
        Assert.Equal("https://example.com/novel/chapter1.html#section1", url);
    }

    [Fact]
    public void ResolveUrl_ReturnsNullForEmptyHref()
    {
        var url = XhtmlSanitizer.ResolveUrl("https://example.com/", "");
        Assert.Null(url);
    }

    [Fact]
    public void ResolveUrl_ReturnsNullForWhitespaceHref()
    {
        var url = XhtmlSanitizer.ResolveUrl("https://example.com/", "   ");
        Assert.Null(url);
    }
}
