using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NovelToEink.Core;

/// <summary>タイトル検索でヒットした作品。</summary>
/// <param name="Site">掲載サイト。</param>
/// <param name="Title">作品タイトル。</param>
/// <param name="Author">作者名。</param>
/// <param name="Url">目次URL。</param>
public sealed record NovelSearchResult(NovelSite Site, string Title, string Author, string Url)
{
    /// <summary>UI 表示用のラベル。</summary>
    public string Label => string.IsNullOrWhiteSpace(Author) ? Title : $"{Title} — {Author}";
}

/// <summary>
/// 作品タイトルから目次URLを探す。
/// なろうは公式 API（api.syosetu.com）、カクヨムは検索ページのスクレイピングを使う。
/// </summary>
public static partial class NovelSearchService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = true,
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ja-JP,ja;q=0.9");
        return client;
    }

    /// <summary>入力がURLらしいか（スキームまたは既知ホストで始まるか）。</summary>
    public static bool LooksLikeUrl(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        var s = input.Trim();
        return s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("ncode.syosetu.com", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("novel18.syosetu.com", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("kakuyomu.jp", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// タイトルで検索し、候補を返す。なろうを優先し、足りなければカクヨムを足す。
    /// </summary>
    /// <param name="query">作品タイトル（作者名を含んでもよい）。</param>
    /// <param name="maxCount">返す候補の最大数。</param>
    /// <param name="ct">キャンセル用トークン。</param>
    public static async Task<IReadOnlyList<NovelSearchResult>> SearchAsync(
        string query, int maxCount = 5, CancellationToken ct = default)
    {
        // 検索APIは関連度順とは限らないので、多めに取ってからこちらで並べ替える。
        const int fetchCount = 20;

        var results = new List<NovelSearchResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in await SearchSyosetuAsync(query, fetchCount, ct).ConfigureAwait(false))
            if (seen.Add(r.Url)) results.Add(r);

        foreach (var r in await SearchKakuyomuAsync(query, fetchCount, ct).ConfigureAwait(false))
            if (seen.Add(r.Url)) results.Add(r);

        return Rank(results, query, maxCount);
    }

    /// <summary>
    /// タイトルから目次URLを 1 件に解決する。見つからなければ null。
    /// </summary>
    public static async Task<NovelSearchResult?> ResolveAsync(
        string query, CancellationToken ct = default)
        => (await SearchAsync(query, 5, ct).ConfigureAwait(false)).FirstOrDefault();

    // --- 小説家になろう（公式 API） ---

    private static async Task<List<NovelSearchResult>> SearchSyosetuAsync(
        string query, int maxCount, CancellationToken ct)
    {
        // まず作品名だけを対象に検索する。タイトルの一部しか渡されないと 0 件になるので、
        // その場合はあらすじ・キーワードも含む既定の検索へ広げる。
        var byTitle = await QuerySyosetuAsync(query, maxCount, titleOnly: true, ct).ConfigureAwait(false);
        if (byTitle.Count > 0) return byTitle;
        return await QuerySyosetuAsync(query, maxCount, titleOnly: false, ct).ConfigureAwait(false);
    }

    private static async Task<List<NovelSearchResult>> QuerySyosetuAsync(
        string query, int maxCount, bool titleOnly, CancellationToken ct)
    {
        var results = new List<NovelSearchResult>();
        try
        {
            // of= で必要な項目だけ受け取る（t:タイトル / n:Nコード / w:作者名）。
            var url = "https://api.syosetu.com/novelapi/api/"
                      + $"?out=json&lim={Math.Clamp(maxCount, 1, 20)}&of=t-n-w"
                      + (titleOnly ? "&title=1" : "")
                      + $"&word={Uri.EscapeDataString(query)}";

            var json = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return results;

            // 先頭要素は {"allcount":N} なので読み飛ばす。
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("ncode", out var ncodeProp)) continue;
                var ncode = ncodeProp.GetString();
                if (string.IsNullOrWhiteSpace(ncode)) continue;

                var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var writer = item.TryGetProperty("writer", out var w) ? w.GetString() ?? "" : "";

                results.Add(new NovelSearchResult(
                    NovelSite.Syosetu, title, writer,
                    $"https://ncode.syosetu.com/{ncode.ToLowerInvariant()}/"));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { /* 検索できなければ候補なしとして扱う */ }

        return results;
    }

    // --- カクヨム（検索ページ） ---

    private static async Task<List<NovelSearchResult>> SearchKakuyomuAsync(
        string query, int maxCount, CancellationToken ct)
    {
        var results = new List<NovelSearchResult>();
        try
        {
            var html = await Http.GetStringAsync(
                $"https://kakuyomu.jp/search?q={Uri.EscapeDataString(query)}", ct).ConfigureAwait(false);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in WorkLinkRegex().Matches(html))
            {
                var title = WebUtility.HtmlDecode(m.Groups["title"].Value);
                var workId = m.Groups["id"].Value;
                if (string.IsNullOrWhiteSpace(title) || !seen.Add(workId)) continue;

                results.Add(new NovelSearchResult(
                    NovelSite.Kakuyomu, title, FindAuthorFor(html, workId),
                    $"https://kakuyomu.jp/works/{workId}"));

                if (results.Count >= maxCount) break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { /* 検索できなければ候補なしとして扱う */ }

        return results;
    }

    /// <summary>埋め込み JSON から作品IDに対応する作者名を拾う。取れなければ空文字。</summary>
    private static string FindAuthorFor(string html, string workId)
    {
        // "Work:<id>":{ ... "author":{"__ref":"UserAccount:<uid>"} ... } を辿って activityName を引く。
        var work = Regex.Match(html,
            $@"""Work:{Regex.Escape(workId)}"":\{{.*?""author"":\{{""__ref"":""UserAccount:(?<uid>\d+)""");
        if (!work.Success) return "";

        var user = Regex.Match(html,
            $@"""UserAccount:{work.Groups["uid"].Value}"":\{{[^}}]*?""activityName"":""(?<name>[^""]*)""");
        return user.Success ? WebUtility.HtmlDecode(user.Groups["name"].Value) : "";
    }

    // --- 並べ替え ---

    /// <summary>タイトルが検索語に近いものを先頭に寄せる。</summary>
    private static List<NovelSearchResult> Rank(List<NovelSearchResult> results, string query, int maxCount)
    {
        var normalizedQuery = Normalize(query);
        return [.. results
            .OrderByDescending(r => Normalize(r.Title) == normalizedQuery)
            .ThenByDescending(r => Normalize(r.Title).Contains(normalizedQuery, StringComparison.Ordinal))
            .ThenBy(r => Normalize(r.Title).Length)
            .Take(maxCount)];
    }

    private static string Normalize(string s)
        => new(s.Where(c => !char.IsWhiteSpace(c) && !char.IsPunctuation(c)).ToArray());

    // 検索結果の見出しリンク: <a title="作品名" href="/works/123456">
    [GeneratedRegex(@"<a\s+title=""(?<title>[^""]*)""\s+href=""/works/(?<id>\d+)""")]
    private static partial Regex WorkLinkRegex();
}
