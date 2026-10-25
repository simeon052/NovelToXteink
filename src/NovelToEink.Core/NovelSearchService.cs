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
    /// <summary>話数（取得できないときは 0）。</summary>
    public int EpisodeCount { get; init; }

    /// <summary>完結しているか（true=完結、false=連載中、null=取得できず・表示は「(不明)」）。</summary>
    public bool? IsCompleted { get; init; }

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
            // of= で必要な項目だけ受け取る（t:タイトル / n:Nコード / w:作者名 / ga:総投稿話数 / e:完結状態）。
            var url = "https://api.syosetu.com/novelapi/api/"
                      + $"?out=json&lim={Math.Clamp(maxCount, 1, 20)}&of=t-n-w-ga-e"
                      + (titleOnly ? "&title=1" : "")
                      + $"&word={Uri.EscapeDataString(query)}";

            var json = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
            results.AddRange(ParseSyosetuJson(json));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        { CoreLog.Warn("作品の検索に失敗（候補なしとして扱う）", ex); }

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

            results.AddRange(ParseKakuyomuHtml(html, maxCount));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        { CoreLog.Warn("作品の検索に失敗（候補なしとして扱う）", ex); }

        return results;
    }

    // --- パース処理（テスト可能なよう内部公開） ---

    /// <summary>novel API の JSON 応答を解析する（先頭要素は {"allcount":N} ヘッダなので読み飛ばす）。</summary>
    internal static List<NovelSearchResult> ParseSyosetuJson(string json)
    {
        var results = new List<NovelSearchResult>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return results;

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var ncode = GetStr(item, "ncode");
            if (string.IsNullOrWhiteSpace(ncode)) continue;

            results.Add(new NovelSearchResult(
                NovelSite.Syosetu, GetStr(item, "title") ?? "", GetStr(item, "writer") ?? "",
                $"https://ncode.syosetu.com/{ncode.ToLowerInvariant()}/")
            {
                EpisodeCount = GetInt(item, "general_all_no"),  // ga: 総投稿話数
                IsCompleted = GetNullableInt(item, "end") is int end ? end == 0 : null,  // e: 0=完結, 1=連載中（取得不能は null）
            });
        }
        return results;
    }

    /// <summary>カクヨム検索ページの HTML から作品リストを抽出する（埋め込み JSON エンティティ優先、正規表現フォールバック）。</summary>
    internal static List<NovelSearchResult> ParseKakuyomuHtml(string html, int maxCount)
    {
        var results = new List<NovelSearchResult>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in WorkLinkRegex().Matches(html))
        {
            var title = WebUtility.HtmlDecode(m.Groups["title"].Value);
            var workId = m.Groups["id"].Value;
            if (string.IsNullOrWhiteSpace(title) || !seen.Add(workId)) continue;

            string author = "";
            int episodes = 0;
            bool? completed = null;

            if (FindEntity(html, $"Work:{workId}") is JsonElement work)
            {
                episodes = GetInt(work, "publicEpisodeCount");
                if (GetStr(work, "serialStatus") is string status)
                    completed = string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase);

                if (work.TryGetProperty("author", out var authorRef) && authorRef.ValueKind == JsonValueKind.Object
                    && GetStr(authorRef, "__ref") is { } refKey && FindEntity(html, refKey) is JsonElement user)
                {
                    author = GetStr(user, "activityName") ?? GetStr(user, "name") ?? "";
                }
            }
            if (string.IsNullOrEmpty(author)) author = FindAuthorFor(html, workId);

            results.Add(new NovelSearchResult(NovelSite.Kakuyomu, title, author,
                $"https://kakuyomu.jp/works/{workId}")
            { EpisodeCount = episodes, IsCompleted = completed });

            if (results.Count >= maxCount) break;
        }
        return results;
    }

    /// <summary>`"key":{...}` の JSON エンティティを波括弧の深さで切り出して解析する（文字列内の括弧は無視）。</summary>
    private static JsonElement? FindEntity(string html, string key)
    {
        int pos = html.IndexOf($"\"{key}\":", StringComparison.Ordinal);
        if (pos < 0) return null;
        int start = html.IndexOf('{', pos + key.Length + 2);
        if (start < 0) return null;

        int depth = 0;
        bool inString = false;
        for (int i = start; i < html.Length; i++)
        {
            char c = html[i];
            if (inString)
            {
                if (c == '\\') { i++; continue; }
                if (c == '"') inString = false;
                continue;
            }
            switch (c)
            {
                case '"': inString = true; break;
                case '{': depth++; break;
                case '}':
                    if (--depth == 0)
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(html.Substring(start, i - start + 1));
                            return doc.RootElement.Clone();
                        }
                        catch (JsonException) { return null; }
                    }
                    break;
            }
        }
        return null;
    }

    private static string? GetStr(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static int GetInt(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p)) return 0;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int n)) return n;
        if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out n)) return n;
        return 0;
    }

    private static int? GetNullableInt(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int n)) return n;
        if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out n)) return n;
        return null;
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
    internal static List<NovelSearchResult> Rank(List<NovelSearchResult> results, string query, int maxCount)
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
