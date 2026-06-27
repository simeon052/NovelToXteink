using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace NovelToEink.Core;

/// <summary>小説家になろう (syosetu.com) 用スクレイパー。</summary>
public sealed partial class SyosetuScraper(HttpFetcher fetcher) : INovelScraper
{
    private readonly HttpFetcher _fetcher = fetcher;

    public NovelSite Site => NovelSite.Syosetu;

    [GeneratedRegex(@"^https?://(ncode|novel18)\.syosetu\.com/([nN]\d+[a-z]+)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    public bool CanHandle(string url) => UrlRegex().IsMatch(url);

    public (NovelSite site, string workId)? TryParseWorkId(string url)
    {
        var m = UrlRegex().Match(url);
        if (!m.Success) return null;
        return (NovelSite.Syosetu, m.Groups[2].Value.ToLowerInvariant());
    }

    private static (string host, string ncode) Parse(string url)
    {
        var m = UrlRegex().Match(url);
        if (!m.Success) throw new ArgumentException($"なろうのURLとして解釈できません: {url}");
        return (m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value.ToLowerInvariant());
    }

    private static string TocUrl(string host, string ncode) => $"https://{host}.syosetu.com/{ncode}/";

    public async Task<NovelMetadata> GetMetadataAsync(string url, CancellationToken ct = default)
    {
        var (host, ncode) = Parse(url);
        var html = await _fetcher.GetStringAsync(TocUrl(host, ncode), ct).ConfigureAwait(false);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var root = doc.DocumentNode;

        var title = TextOf(root, ".//h1[contains(@class,'p-novel__title')]", ".//p[contains(@class,'novel_title')]", ".//h1")
                    ?? "（無題）";
        var author = TextOf(root, ".//div[contains(@class,'p-novel__author')]//a",
                                   ".//div[contains(@class,'p-novel__author')]",
                                   ".//div[@class='novel_writername']//a",
                                   ".//div[@class='novel_writername']") ?? "";
        author = author.Replace("作者：", "").Replace("作者:", "").Trim();

        var desc = TextOf(root, ".//div[@id='novel_ex']", ".//div[contains(@class,'p-novel__summary')]") ?? "";

        var (completed, lastup) = await FetchNarouStatusAsync(host, ncode, ct).ConfigureAwait(false);

        return new NovelMetadata
        {
            Site = NovelSite.Syosetu,
            WorkId = ncode,
            Title = title.Trim(),
            Author = author,
            Description = desc.Trim(),
            Url = TocUrl(host, ncode),
            OfficialCoverUrl = null, // なろうに公式表紙は無い
            IsCompleted = completed,
            SiteLastUpdated = lastup,
        };
    }

    /// <summary>なろう公式APIから完結状態(end)と最終更新日(general_lastup)を取得。</summary>
    private async Task<(bool Completed, DateTimeOffset? LastUp)> FetchNarouStatusAsync(
        string host, string ncode, CancellationToken ct)
    {
        var apiHost = host == "novel18" ? "novel18api" : "novelapi";
        var url = $"https://api.syosetu.com/{apiHost}/api/?out=json&gzip=0&of=e-nt-gl&ncode={ncode}";
        try
        {
            var json = await _fetcher.GetStringAsync(url, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 2) return (false, null);
            var o = root[1];
            var end = o.TryGetProperty("end", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 1;
            DateTimeOffset? lastup = null;
            if (o.TryGetProperty("general_lastup", out var gl) && gl.ValueKind == JsonValueKind.String &&
                DateTime.TryParseExact(gl.GetString(), "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                lastup = new DateTimeOffset(dt, TimeSpan.FromHours(9)); // JST
            return (end == 0, lastup); // end==0 → 完結済/短編
        }
        catch
        {
            return (false, null);
        }
    }

    public async Task<IReadOnlyList<EpisodeRef>> GetTableOfContentsAsync(string url, CancellationToken ct = default)
    {
        var (host, ncode) = Parse(url);
        var baseToc = TocUrl(host, ncode);
        var result = new List<EpisodeRef>();
        var seen = new HashSet<string>();
        var index = 0;
        string? currentChapter = null;

        var page = 1;
        const int maxPages = 500;
        while (page <= maxPages)
        {
            ct.ThrowIfCancellationRequested();
            var pageUrl = page == 1 ? baseToc : $"{baseToc}?p={page}";
            var html = await _fetcher.GetStringAsync(pageUrl, ct).ConfigureAwait(false);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var root = doc.DocumentNode;

            // 章見出しとエピソードリンクを文書順で取得（新旧レイアウト両対応）。
            const string xpath =
                ".//div[contains(@class,'p-eplist__chapter-title')]" +
                " | .//a[contains(@class,'p-eplist__subtitle')]" +
                " | .//div[contains(@class,'chapter_title')]" +
                " | .//dd[contains(@class,'subtitle')]/a";
            var nodes = root.SelectNodes(xpath);
            var addedOnPage = 0;
            if (nodes != null)
            {
                foreach (var n in nodes)
                {
                    var cls = n.GetAttributeValue("class", "");
                    if (cls.Contains("chapter-title") || cls.Contains("chapter_title"))
                    {
                        currentChapter = HtmlEntity.DeEntitize(n.InnerText).Trim();
                    }
                    else // エピソードのアンカー
                    {
                        var href = n.GetAttributeValue("href", "");
                        var abs = XhtmlSanitizer.ResolveUrl(baseToc, href);
                        if (abs is null || !seen.Add(abs)) continue;
                        index++;
                        result.Add(new EpisodeRef
                        {
                            Index = index,
                            Title = HtmlEntity.DeEntitize(n.InnerText).Trim(),
                            Url = abs,
                            ChapterTitle = currentChapter,
                        });
                        addedOnPage++;
                    }
                }
            }

            // 次ページの有無を判定。
            var hasNext = root.SelectSingleNode(
                ".//a[contains(@class,'c-pager__item--next')]" +
                " | .//a[contains(@class,'novelview_pager-next')]") != null;
            if (!hasNext || addedOnPage == 0) break;
            page++;
        }

        return result;
    }

    public async Task<EpisodeContent> GetEpisodeAsync(EpisodeRef episode, EpubOptions options, CancellationToken ct = default)
    {
        var html = await _fetcher.GetStringAsync(episode.Url, ct).ConfigureAwait(false);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var root = doc.DocumentNode;

        var bodyNode =
            FirstNode(root,
                ".//div[contains(@class,'p-novel__text') and not(contains(@class,'preface')) and not(contains(@class,'afterword'))]",
                ".//div[@id='novel_honbun']");
        var prefaceNode =
            FirstNode(root,
                ".//div[contains(@class,'p-novel__text--preface')]",
                ".//div[@id='novel_p']");
        var afterwordNode =
            FirstNode(root,
                ".//div[contains(@class,'p-novel__text--afterword')]",
                ".//div[@id='novel_a']");

        var images = new List<string>();
        string body;
        if (bodyNode != null)
        {
            var (xhtml, imgs) = XhtmlSanitizer.Clean(bodyNode, episode.Url, options);
            body = xhtml;
            images.AddRange(imgs);
        }
        else
        {
            body = "<p><br/></p>";
        }

        string? foreword = null;
        if (prefaceNode != null)
        {
            var (xhtml, imgs) = XhtmlSanitizer.Clean(prefaceNode, episode.Url, options);
            foreword = xhtml;
            images.AddRange(imgs);
        }
        string? afterword = null;
        if (afterwordNode != null)
        {
            var (xhtml, imgs) = XhtmlSanitizer.Clean(afterwordNode, episode.Url, options);
            afterword = xhtml;
            images.AddRange(imgs);
        }

        return new EpisodeContent
        {
            Ref = episode,
            ForewordHtml = foreword,
            BodyHtml = body,
            AfterwordHtml = afterword,
            ImageUrls = images,
        };
    }

    private static HtmlNode? FirstNode(HtmlNode root, params string[] xpaths)
    {
        foreach (var xp in xpaths)
        {
            var n = root.SelectSingleNode(xp);
            if (n != null) return n;
        }
        return null;
    }

    private static string? TextOf(HtmlNode root, params string[] xpaths)
    {
        var n = FirstNode(root, xpaths);
        if (n == null) return null;
        var t = HtmlEntity.DeEntitize(n.InnerText);
        return string.IsNullOrWhiteSpace(t) ? null : t.Trim();
    }
}
