using System.Net;

namespace NovelToEink.Core;

/// <summary>
/// スクレイピング用の薄い HTTP ラッパー。
/// ブラウザ風 User-Agent、リクエスト間ウェイト、簡単なリトライを行う。
/// </summary>
public sealed class HttpFetcher : IDisposable
{
    private readonly HttpClient _http;
    private readonly int _delayMs;
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public HttpFetcher(int delayMs = 1500)
    {
        _delayMs = delayMs;
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            CookieContainer = new CookieContainer(),
            UseCookies = true,
        };
        // なろうの R18 サイト(novel18)向け年齢確認クッキー。一般作品では無害。
        handler.CookieContainer.Add(new Cookie("over18", "yes", "/", ".syosetu.com"));
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
        var h = _http.DefaultRequestHeaders;
        h.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        h.AcceptLanguage.ParseAdd("ja,en;q=0.8");
        h.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
    }

    /// <summary>HTMLテキストを取得（UTF-8前提、明示文字コードがあれば尊重）。</summary>
    public async Task<string> GetStringAsync(string url, CancellationToken ct = default)
    {
        using var res = await SendWithRetryAsync(url, ct).ConfigureAwait(false);
        var bytes = await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        var charset = res.Content.Headers.ContentType?.CharSet;
        var enc = ResolveEncoding(charset);
        return enc.GetString(bytes);
    }

    /// <summary>バイナリ取得（画像用）。</summary>
    public async Task<byte[]> GetBytesAsync(string url, CancellationToken ct = default)
    {
        using var res = await SendWithRetryAsync(url, ct).ConfigureAwait(false);
        return await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(string url, CancellationToken ct)
    {
        const int maxAttempts = 3;
        HttpRequestException? last = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await ThrottleAsync(ct).ConfigureAwait(false);
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if ((int)res.StatusCode == 429 || (int)res.StatusCode >= 500)
                {
                    res.Dispose();
                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(2000 * attempt, ct).ConfigureAwait(false);
                        continue;
                    }
                    throw new HttpRequestException($"HTTP {(int)res.StatusCode} after {attempt} attempts: {url}");
                }
                res.EnsureSuccessStatusCode();
                return res;
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                last = ex;
                await Task.Delay(2000 * attempt, ct).ConfigureAwait(false);
            }
        }
        throw last ?? new HttpRequestException($"Failed to fetch {url}");
    }

    private async Task ThrottleAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var elapsed = DateTimeOffset.UtcNow - _lastRequest;
            var wait = _delayMs - (int)elapsed.TotalMilliseconds;
            if (wait > 0)
                await Task.Delay(wait, ct).ConfigureAwait(false);
            _lastRequest = DateTimeOffset.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static System.Text.Encoding ResolveEncoding(string? charset)
    {
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try { return System.Text.Encoding.GetEncoding(charset.Trim('"')); }
            catch { /* fall through */ }
        }
        return System.Text.Encoding.UTF8;
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
