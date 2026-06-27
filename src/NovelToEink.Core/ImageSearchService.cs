using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace NovelToEink.Core;

/// <summary>
/// Bing / Google の画像検索から表紙候補画像を取得する。
/// スロットリングなし（検索用途のため）、別 HttpClient を使用する。
/// </summary>
public static class ImageSearchService
{
    private static readonly HttpClient _http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            AllowAutoRedirect = true,
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        var h = client.DefaultRequestHeaders;
        h.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        h.AcceptLanguage.ParseAdd("ja-JP,ja;q=0.9,en-US;q=0.7,en;q=0.5");
        h.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
        h.Add("Accept-Encoding", "gzip, deflate, br");
        return client;
    }

    /// <summary>
    /// タイトル・作者から画像検索を行い、上位 maxCount 枚を返す。
    /// Bing をプライマリ、Google をフォールバックとして使用する。
    /// </summary>
    public static async Task<List<ScrapedImage>> SearchAsync(
        string title, string author, int maxCount = 5, CancellationToken ct = default)
    {
        var query = string.IsNullOrWhiteSpace(author)
            ? $"{title} 小説 表紙"
            : $"{title} {author} 小説 表紙";

        var imageUrls = await FetchImageUrlsAsync(query, ct).ConfigureAwait(false);

        var results = new List<ScrapedImage>();
        foreach (var url in imageUrls)
        {
            if (results.Count >= maxCount) break;
            ct.ThrowIfCancellationRequested();
            try
            {
                using var imgCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                imgCts.CancelAfter(TimeSpan.FromSeconds(15));
                var bytes = await _http.GetByteArrayAsync(url, imgCts.Token).ConfigureAwait(false);
                var img = ImageProcessor.TryLoad(bytes, url);
                if (img != null) results.Add(img);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* 取得失敗は無視して次へ */ }
        }
        return results;
    }

    private static async Task<List<string>> FetchImageUrlsAsync(string query, CancellationToken ct)
    {
        var urls = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Primary: Bing Images
        try
        {
            var encoded = Uri.EscapeDataString(query);
            var html = await _http.GetStringAsync(
                $"https://www.bing.com/images/search?q={encoded}&FORM=HDRSC2&first=1",
                ct).ConfigureAwait(false);
            foreach (var u in ParseBingUrls(html))
                if (seen.Add(u)) urls.Add(u);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { }

        // Fallback: Google Images
        if (urls.Count < 5)
        {
            try
            {
                var encoded = Uri.EscapeDataString(query);
                var html = await _http.GetStringAsync(
                    $"https://www.google.co.jp/search?q={encoded}&tbm=isch&hl=ja",
                    ct).ConfigureAwait(false);
                foreach (var u in ParseGoogleUrls(html))
                    if (seen.Add(u)) urls.Add(u);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }
        }

        return urls;
    }

    // --- Bing ---

    private static IEnumerable<string> ParseBingUrls(string html)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Strategy 1: HTML entity-encoded attribute — &quot;murl&quot;:&quot;URL&quot;
        foreach (Match m in Regex.Matches(html, @"&quot;murl&quot;:&quot;(https://[^&""<\s]+)&quot;"))
        {
            var url = CleanUrl(m.Groups[1].Value);
            if (IsValidImageUrl(url) && seen.Add(url)) yield return url;
        }

        // Strategy 2: JSON in <script> — "murl":"URL"
        foreach (Match m in Regex.Matches(html, @"""murl"":""(https://[^""\\<\s]+)"""))
        {
            var url = CleanUrl(UnescapeJson(m.Groups[1].Value));
            if (IsValidImageUrl(url) && seen.Add(url)) yield return url;
        }

        // Strategy 3: HtmlAgilityPack で m 属性を entity decode してから探す
        if (!seen.Any())
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            foreach (var node in doc.DocumentNode.SelectNodes("//*[@m]") ?? Enumerable.Empty<HtmlNode>())
            {
                var attr = node.GetAttributeValue("m", "");
                if (string.IsNullOrEmpty(attr)) continue;
                var match = Regex.Match(attr, @"""murl"":""(https://[^""]+)""");
                if (!match.Success) continue;
                var url = CleanUrl(UnescapeJson(match.Groups[1].Value));
                if (IsValidImageUrl(url) && seen.Add(url)) yield return url;
            }
        }
    }

    // --- Google Images ---

    private static IEnumerable<string> ParseGoogleUrls(string html)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // "ou":"URL" — original URL field in embedded JSON
        foreach (Match m in Regex.Matches(html, @"""ou"":""(https://[^""\\<\s]+)"""))
        {
            var url = CleanUrl(UnescapeJson(m.Groups[1].Value));
            if (IsValidImageUrl(url) && seen.Add(url)) yield return url;
        }
    }

    // --- helpers ---

    private static string UnescapeJson(string s)
    {
        s = s.Replace(@"\/", "/");
        // \uXXXX 形式のユニコードエスケープを展開（例: = → =）
        s = Regex.Replace(s, @"\\u([0-9a-fA-F]{4})",
            m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
        return s;
    }

    private static string CleanUrl(string url)
    {
        var idx = url.IndexOfAny(['"', '<', '>', ' ', '\r', '\n']);
        return idx >= 0 ? url[..idx] : url;
    }

    private static bool IsValidImageUrl(string url)
        => !string.IsNullOrEmpty(url)
           && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
           && url.Length < 2000;
}
