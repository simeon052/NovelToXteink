namespace NovelToEink.Core;

public sealed class DownloadProgress
{
    public required string Phase { get; init; }   // Locale.T("meta_fetch") Locale.T("fetch_toc") Locale.T("fetch_body") Locale.T("fetch_image")
    public int Current { get; init; }
    public int Total { get; init; }
    public string Message { get; init; } = "";
}

/// <summary>URL→スクレイパー選択、メタ/目次/本文/画像のダウンロードを統括する。</summary>
public sealed class NovelDownloadService : IDisposable, IGetScraper
{
    private readonly HttpFetcher _fetcher;
    private readonly IReadOnlyList<INovelScraper> _scrapers;
    private readonly IGetScraper? _factory;

    public NovelDownloadService(DownloadOptions? download = null, IGetScraper? factory = null)
    {
        download ??= new DownloadOptions();
        _fetcher = new HttpFetcher(download.RequestDelayMs);
        _scrapers = [new SyosetuScraper(_fetcher), new KakuyomuScraper(_fetcher)];
        _factory = factory;
    }

    public bool IsSupported(string url) => _scrapers.Any(s => s.CanHandle(url));

    /// <summary>URL→スクレイパー選択。factory（テストseam）があればそれを優先。</summary>
    public INovelScraper GetScraper(string url)
        => _factory?.GetScraper(url)
           ?? _scrapers.FirstOrDefault(s => s.CanHandle(url))
           ?? throw new NotSupportedException(Locale.T("unsupported_url"));

    public Task<NovelMetadata> GetMetadataAsync(string url, CancellationToken ct = default)
        => GetScraper(url).GetMetadataAsync(url, ct);

    public Task<IReadOnlyList<EpisodeRef>> GetTableOfContentsAsync(string url, CancellationToken ct = default)
        => GetScraper(url).GetTableOfContentsAsync(url, ct);

    /// <summary>
    /// 本文と画像をまとめて取得する。
    /// cacheRoot を指定するとエピソード・画像・メタデータをキャッシュし、差分のみネットワーク取得する。
    /// </summary>
    /// <param name="forceRefreshMeta">true のときメタデータキャッシュを無視して常に最新を取得する。</param>
    public async Task<NovelDownload> DownloadAsync(
        string url,
        EpubOptions epubOptions,
        DownloadOptions downloadOptions,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default,
        string? cacheRoot = null,
        bool forceRefreshMeta = false)
    {
        var scraper = GetScraper(url);

        // URLからワークIDを解析し、メタ取得前にキャッシュを初期化できるようにする
        var workInfo = scraper.TryParseWorkId(url);
        DownloadCache? cache = (cacheRoot != null && workInfo != null)
            ? new DownloadCache(cacheRoot, workInfo.Value.site.ToString(), workInfo.Value.workId)
            : null;

        // メタデータ（キャッシュ優先。forceRefreshMeta または TTL 超過時はネット取得）
        NovelMetadata meta;
        var cachedMeta = forceRefreshMeta ? null : cache?.TryGetMetadata();
        if (cachedMeta != null)
        {
            meta = cachedMeta;
            progress?.Report(new DownloadProgress { Phase = Locale.T("meta_fetch"), Message = Locale.T("cache_meta") });
        }
        else
        {
            progress?.Report(new DownloadProgress { Phase = Locale.T("meta_fetch"), Message = Locale.T("fetching_work_info") });
            meta = await scraper.GetMetadataAsync(url, ct).ConfigureAwait(false);
            // workInfo が null だったケース（念のため）はここでキャッシュを初期化
            if (cache == null && cacheRoot != null)
                cache = new DownloadCache(cacheRoot, meta.Site.ToString(), meta.WorkId);
            cache?.SetMetadata(meta);
        }

        progress?.Report(new DownloadProgress { Phase = Locale.T("fetch_toc"), Message = Locale.T("fetching_toc") });
        var toc = await scraper.GetTableOfContentsAsync(url, ct).ConfigureAwait(false);

        if (downloadOptions.MaxEpisodes > 0 && toc.Count > downloadOptions.MaxEpisodes)
            toc = toc.Take(downloadOptions.MaxEpisodes).ToList();

        // --- 本文取得（キャッシュ優先）---
        var episodes = new List<EpisodeContent>(toc.Count);
        var cacheHits = 0;

        for (var i = 0; i < toc.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var ep = toc[i];

            // キャッシュ確認
            var cached = cache?.TryGetEpisode(ep);
            if (cached != null)
            {
                episodes.Add(cached);
                cacheHits++;
                // キャッシュは50話まとめて1回報告（高速なので毎回は不要）
                if ((i + 1) % 50 == 0 || i == toc.Count - 1)
                    progress?.Report(new DownloadProgress
                    {
                        Phase = Locale.T("fetch_body"),
                        Current = i + 1,
                        Total = toc.Count,
                        Message = Locale.T("cache_read_fmt", i + 1, toc.Count),
                    });
                continue;
            }

            // ネットワーク取得（1リクエストごとに報告）
            progress?.Report(new DownloadProgress
            {
                Phase = Locale.T("fetch_body"),
                Current = i + 1,
                Total = toc.Count,
                Message = $"[{i + 1}/{toc.Count}] {ep.Title}",
            });
            var content = await scraper.GetEpisodeAsync(ep, epubOptions, ct).ConfigureAwait(false);
            episodes.Add(content);
            cache?.SetEpisode(content);
        }

        if (cacheHits > 0)
            progress?.Report(new DownloadProgress
            {
                Phase = Locale.T("fetch_body"),
                Current = toc.Count,
                Total = toc.Count,
                Message = Locale.T("fetch_done_fmt", cacheHits, toc.Count - cacheHits),
            });

        // --- 画像（公式表紙＋本文挿絵）の収集 ---
        var images = new List<ScrapedImage>();
        if (downloadOptions.DownloadImages)
        {
            // 公式表紙
            if (!string.IsNullOrWhiteSpace(meta.OfficialCoverUrl))
            {
                ct.ThrowIfCancellationRequested();
                var cachedBytes = cache?.TryGetImage(meta.OfficialCoverUrl!);
                if (cachedBytes != null)
                {
                    var img = ImageProcessor.TryLoad(cachedBytes, meta.OfficialCoverUrl!, 0, true);
                    if (img != null) images.Add(img);
                }
                else
                {
                    progress?.Report(new DownloadProgress { Phase = Locale.T("fetch_image"), Message = Locale.T("fetching_official_cover") });
                    var img = await TryDownloadImageAsync(meta.OfficialCoverUrl!, 0, true, ct).ConfigureAwait(false);
                    if (img != null)
                    {
                        images.Add(img);
                        cache?.SetImage(meta.OfficialCoverUrl!, img.Data);
                    }
                }
            }

            // 本文中の挿絵（重複URLは1回だけ）
            var urlFirstEpisode = new Dictionary<string, int>();
            foreach (var ep in episodes)
                foreach (var u in ep.ImageUrls)
                    urlFirstEpisode.TryAdd(u, ep.Ref.Index);

            var idx = 0;
            foreach (var (u, epIndex) in urlFirstEpisode)
            {
                ct.ThrowIfCancellationRequested();
                idx++;

                var cachedBytes = cache?.TryGetImage(u);
                if (cachedBytes != null)
                {
                    var img = ImageProcessor.TryLoad(cachedBytes, u, epIndex, false);
                    if (img != null) images.Add(img);
                    continue;
                }

                progress?.Report(new DownloadProgress
                {
                    Phase = Locale.T("fetch_image"),
                    Current = idx,
                    Total = urlFirstEpisode.Count,
                    Message = Locale.T("illus_prog_fmt", idx, urlFirstEpisode.Count),
                });
                var imgNet = await TryDownloadImageAsync(u, epIndex, false, ct).ConfigureAwait(false);
                if (imgNet != null)
                {
                    images.Add(imgNet);
                    cache?.SetImage(u, imgNet.Data);
                }
            }
        }

        return new NovelDownload { Metadata = meta, Episodes = episodes, Images = images };
    }

    private async Task<ScrapedImage?> TryDownloadImageAsync(string url, int episodeIndex, bool isOfficialCover, CancellationToken ct)
    {
        try
        {
            var bytes = await _fetcher.GetBytesAsync(url, ct).ConfigureAwait(false);
            return ImageProcessor.TryLoad(bytes, url, episodeIndex, isOfficialCover);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // HttpClient のタイムアウトは TaskCanceledException（OperationCanceledException の子）で来る。
            // 利用者の中断（ct が取り消し済み）だけを伝え、それ以外は画像1枚の失敗として続行する。
            CoreLog.Ignored($"画像を取得できない: {url}", ex);
            return null; // 画像1枚の失敗は致命ではない。
        }
    }

    public void Dispose() => _fetcher.Dispose();
}
