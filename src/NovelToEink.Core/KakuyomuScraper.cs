using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace NovelToEink.Core;

/// <summary>カクヨム (kakuyomu.jp) 用スクレイパー。目次は __NEXT_DATA__(Apollo) から復元する。</summary>
public sealed partial class KakuyomuScraper(HttpFetcher fetcher) : INovelScraper
{
    private readonly HttpFetcher _fetcher = fetcher;

    public NovelSite Site => NovelSite.Kakuyomu;

    [GeneratedRegex(@"^https?://kakuyomu\.jp/works/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    public bool CanHandle(string url) => UrlRegex().IsMatch(url);

    public (NovelSite site, string workId)? TryParseWorkId(string url)
    {
        var m = UrlRegex().Match(url);
        if (!m.Success) return null;
        return (NovelSite.Kakuyomu, m.Groups[1].Value);
    }

    private static string ParseWorkId(string url)
    {
        var m = UrlRegex().Match(url);
        if (!m.Success) throw new ArgumentException($"カクヨムのURLとして解釈できません: {url}");
        return m.Groups[1].Value;
    }

    private static string WorkUrl(string id) => $"https://kakuyomu.jp/works/{id}";

    /// <summary>作品ページを取得し Apollo state を返す（メタ/目次の両方で使う）。</summary>
    private async Task<(NovelMetadata Meta, List<EpisodeRef> Toc)> LoadWorkAsync(string url, CancellationToken ct)
    {
        var workId = ParseWorkId(url);
        var html = await _fetcher.GetStringAsync(WorkUrl(workId), ct).ConfigureAwait(false);
        var state = ExtractApolloState(html)
            ?? throw new InvalidOperationException("カクヨムのページ構造を解析できませんでした(__NEXT_DATA__が見つかりません)。");

        // Work エンティティを特定。
        JsonElement work = default;
        var found = false;
        foreach (var prop in state.EnumerateObject())
        {
            if (prop.Name.StartsWith("Work:", StringComparison.Ordinal) &&
                prop.Value.TryGetProperty("id", out var idEl) &&
                idEl.ValueKind == JsonValueKind.String && idEl.GetString() == workId)
            {
                work = prop.Value;
                found = true;
                break;
            }
        }
        if (!found)
        {
            // フォールバック: 最初の Work:。
            foreach (var prop in state.EnumerateObject())
            {
                if (prop.Name.StartsWith("Work:", StringComparison.Ordinal)) { work = prop.Value; found = true; break; }
            }
        }
        if (!found) throw new InvalidOperationException("作品情報(Work)が見つかりませんでした。");

        var title = GetString(work, "title") ?? "（無題）";
        var description = GetString(work, "introduction") ?? GetString(work, "catchphrase") ?? "";
        var author = ResolveAuthor(state, work);
        var cover = FindCoverUrl(work);

        var completed = string.Equals(GetString(work, "serialStatus"), "COMPLETED", StringComparison.OrdinalIgnoreCase);
        DateTimeOffset? lastup = null;
        if (GetString(work, "lastEpisodePublishedAt") is { } ls &&
            DateTimeOffset.TryParse(ls, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d))
            lastup = d;

        var meta = new NovelMetadata
        {
            Site = NovelSite.Kakuyomu,
            WorkId = workId,
            Title = title.Trim(),
            Author = author,
            Description = description.Trim(),
            Url = WorkUrl(workId),
            OfficialCoverUrl = cover,
            IsCompleted = completed,
            SiteLastUpdated = lastup,
        };

        var toc = BuildToc(state, work, workId);
        return (meta, toc);
    }

    public async Task<NovelMetadata> GetMetadataAsync(string url, CancellationToken ct = default)
        => (await LoadWorkAsync(url, ct).ConfigureAwait(false)).Meta;

    public async Task<IReadOnlyList<EpisodeRef>> GetTableOfContentsAsync(string url, CancellationToken ct = default)
        => (await LoadWorkAsync(url, ct).ConfigureAwait(false)).Toc;

    public async Task<EpisodeContent> GetEpisodeAsync(EpisodeRef episode, EpubOptions options, CancellationToken ct = default)
    {
        var html = await _fetcher.GetStringAsync(episode.Url, ct).ConfigureAwait(false);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var root = doc.DocumentNode;

        var bodyNode =
            root.SelectSingleNode(".//div[contains(@class,'widget-episodeBody')]")
            ?? root.SelectSingleNode(".//div[contains(@class,'js-episode-body')]");

        string body;
        var images = new List<string>();
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

        return new EpisodeContent
        {
            Ref = episode,
            BodyHtml = body,
            ImageUrls = images,
        };
    }

    // ---- Apollo state ヘルパ ----

    private List<EpisodeRef> BuildToc(JsonElement state, JsonElement work, string workId)
    {
        var list = new List<EpisodeRef>();
        var index = 0;
        var seen = new HashSet<string>();

        void AddEpisode(JsonElement ep, string? chapterTitle)
        {
            var epId = GetString(ep, "id");
            if (epId is null || !seen.Add(epId)) return;
            var epTitle = GetString(ep, "title") ?? $"第{index + 1}話";
            index++;
            list.Add(new EpisodeRef
            {
                Index = index,
                Title = epTitle.Trim(),
                Url = $"{WorkUrl(workId)}/episodes/{epId}",
                ChapterTitle = string.IsNullOrWhiteSpace(chapterTitle) ? null : chapterTitle.Trim(),
            });
        }

        // 正攻法: Work.tableOfContents → 章 → episodeUnions の順で復元。
        if (work.TryGetProperty("tableOfContents", out var toc) && toc.ValueKind == JsonValueKind.Array)
        {
            foreach (var chapRef in toc.EnumerateArray())
            {
                var chap = Resolve(state, chapRef);
                if (chap is null) continue;
                var chapEl = chap.Value;

                string? chapterTitle = null;
                if (chapEl.TryGetProperty("chapter", out var cRef))
                {
                    var c = Resolve(state, cRef);
                    if (c != null) chapterTitle = GetString(c.Value, "title");
                }

                var eps = GetArray(chapEl, "episodeUnions") ?? GetArray(chapEl, "episodes");
                if (eps != null)
                {
                    foreach (var epRef in eps.Value.EnumerateArray())
                    {
                        var ep = Resolve(state, epRef);
                        if (ep != null) AddEpisode(ep.Value, chapterTitle);
                    }
                }
            }
        }

        // フォールバック: state 中の Episode を出現順に。
        if (list.Count == 0)
        {
            foreach (var prop in state.EnumerateObject())
            {
                if (prop.Name.StartsWith("Episode:", StringComparison.Ordinal) &&
                    prop.Value.TryGetProperty("title", out _))
                {
                    AddEpisode(prop.Value, null);
                }
            }
        }

        return list;
    }

    private static string ResolveAuthor(JsonElement state, JsonElement work)
    {
        // Work.author → UserAccount.activityName
        if (work.TryGetProperty("author", out var aRef))
        {
            var a = Resolve(state, aRef);
            if (a != null)
            {
                var name = GetString(a.Value, "activityName") ?? GetString(a.Value, "name");
                if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
            }
        }
        return "";
    }

    private static string? FindCoverUrl(JsonElement work)
    {
        // *coverImageUrl* に該当する文字列プロパティを探す。
        foreach (var p in work.EnumerateObject())
        {
            if (p.Name.Contains("overImageUrl", StringComparison.OrdinalIgnoreCase) &&
                p.Value.ValueKind == JsonValueKind.String)
            {
                var v = p.Value.GetString();
                if (!string.IsNullOrWhiteSpace(v) && v.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    return v;
            }
        }
        return null;
    }

    private static JsonElement? Resolve(JsonElement state, JsonElement refToken)
    {
        // {"__ref":"Episode:xxx"} 形式を解決。
        if (refToken.ValueKind == JsonValueKind.Object &&
            refToken.TryGetProperty("__ref", out var r) && r.ValueKind == JsonValueKind.String)
        {
            var key = r.GetString()!;
            if (state.TryGetProperty(key, out var entity)) return entity;
            return null;
        }
        // 直接埋め込みのオブジェクトならそのまま。
        if (refToken.ValueKind == JsonValueKind.Object) return refToken;
        return null;
    }

    private static string? GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static JsonElement? GetArray(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v : null;

    /// <summary>__NEXT_DATA__ から props.pageProps.__APOLLO_STATE__ を取り出す。</summary>
    private static JsonElement? ExtractApolloState(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var script = doc.DocumentNode.SelectSingleNode(".//script[@id='__NEXT_DATA__']");
        if (script == null) return null;
        var json = script.InnerHtml; // script 内は生JSON
        JsonDocument parsed;
        try { parsed = JsonDocument.Parse(json); }
        catch { return null; }

        // 所有権を保持するため、必要部分を切り出してクローンする。
        var rootClone = parsed.RootElement.Clone();
        parsed.Dispose();

        if (rootClone.TryGetProperty("props", out var props) &&
            props.TryGetProperty("pageProps", out var pageProps) &&
            pageProps.TryGetProperty("__APOLLO_STATE__", out var apollo))
        {
            return apollo;
        }
        return null;
    }
}
