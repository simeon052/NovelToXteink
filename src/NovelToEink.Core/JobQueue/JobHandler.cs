namespace NovelToEink.Core;

/// <summary>URL→スクレイパー選択のテスト可能seam（Core）。UI非依存。</summary>
public interface IGetScraper
{
    INovelScraper GetScraper(string url);
}

/// <summary>JobQueueの1ジョブを実装するCoreハンドラ。
/// Check/Convertはキャッシュ-first（キャッシュがあればネットワークを使わない）。
/// 必ず ct を観察する（JobQueue.Cancel と接続するため）。</summary>
public sealed class JobHandler : IJobHandler
{
    private readonly LibraryService _library;
    private readonly IGetScraper _scraper;
    private readonly Func<JobRecord, IReadOnlyList<ScrapedImage>, ScrapedImage?>? _coverResolver;
    private readonly IProgress<string>? _progress;

    public JobHandler(
        LibraryService library,
        IGetScraper scraper,
        Func<JobRecord, IReadOnlyList<ScrapedImage>, ScrapedImage?>? coverResolver = null,
        IProgress<string>? progress = null)
    {
        _library = library;
        _scraper = scraper;
        _coverResolver = coverResolver;
        _progress = progress;
    }

    public async Task<JobResult> ExecuteAsync(JobRecord job, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); // cancelを最も速く観察する
        var settings = _library.Settings;
        var entry = _library.Entries.FirstOrDefault(e =>
            e.Url == job.Url || (job.WorkId != "" && e.WorkId == job.WorkId && e.Site == job.Site));
        var opts = entry?.Options ?? settings.ToEpubOptions();

        switch (job.Action)
        {
            case JobAction.Check:
                if (entry is null) return JobResult.Fail("エントリが見つかりません");
                // CreateEntry直後のエントリはSite/WorkId未解析。job側の情報を引き写す。
                if (entry.WorkId == "") { entry.WorkId = job.WorkId; entry.Site = job.Site; }
                // plan §3: Checkはキャッシュ-first。miss時のみネットワーク。
                var cr = DownloadCache.GetCacheRoot(settings.OutputFolder);
                var siteKey = job.Site.ToString();
                if (DownloadCache.HasCachedWork(cr, siteKey, job.WorkId))
                {
                    var c = new DownloadCache(cr, siteKey, job.WorkId);
                    var meta = c.TryGetMetadata();
                    if (meta is not null)
                    {
                        _progress?.Report("Check: キャッシュを使用");
                        entry.Title = TitleCleaner.Clean(meta.Title) ?? "";
                        entry.Author = meta.Author;
                        entry.IsCompleted = meta.IsCompleted;
                        entry.SiteLastUpdated = meta.SiteLastUpdated;
                        var toc = c.LoadAllCachedEpisodes().Select(x => x.Ref).ToList();
                        var hasUpdate = entry.HasUpdate(toc);
                        entry.LastCheckedAt = DateTimeOffset.Now;
                        entry.Status = hasUpdate ? UpdateStatus.UpdateAvailable : UpdateStatus.UpToDate;
                        _library.Persist();
                        return JobResult.Ok();
                    }
                }
                await _library.CheckAsync(entry, ct, _progress);
                return JobResult.Ok();

            case JobAction.Update:
                if (entry is null) return JobResult.Fail("エントリが見つかりません");
                if (entry.WorkId == "") { entry.WorkId = job.WorkId; entry.Site = job.Site; }
                await _library.UpdateAsync(entry, null, _progress, ct);
                return JobResult.Ok();

            case JobAction.Download:
                if (string.IsNullOrWhiteSpace(job.Url)) return JobResult.Fail("URLがありません");
                var novel = await _library.DownloadAsync(job.Url, opts, null, ct);
                var cover = settings.AutoCover
                    ? novel.Images.FirstOrDefault(c => c.IsOfficialCover) ?? novel.Images.FirstOrDefault()
                    : _coverResolver?.Invoke(job, novel.Images);
                _library.BuildAndRegister(novel, cover, opts, _progress, settings.AutoCover);
                return JobResult.Ok();

            case JobAction.Convert:
                // cache-only: ネットワークに行かない。miss時はキャッシュフォルダを作らず失敗。
                var cacheRoot = DownloadCache.GetCacheRoot(settings.OutputFolder);
                if (!DownloadCache.HasCachedWork(cacheRoot, job.Site.ToString(), job.WorkId))
                    return JobResult.Fail("キャッシュがありません。先にダウンロードしてください");
                var cached = new DownloadCache(cacheRoot, job.Site.ToString(), job.WorkId).LoadCached();
                if (cached is null) return JobResult.Fail("キャッシュがありません。先にダウンロードしてください");
                var cachedCover = settings.AutoCover
                    ? cached.Images.FirstOrDefault(c => c.IsOfficialCover) ?? cached.Images.FirstOrDefault()
                    : _coverResolver?.Invoke(job, cached.Images);
                var built = new NovelDownload
                {
                    Metadata = cached.Metadata,
                    Episodes = cached.Episodes,
                    Images = cached.Images,
                };
                _library.BuildAndRegister(built, cachedCover, opts, _progress, settings.AutoCover);
                return JobResult.Ok();

            default:
                return JobResult.Fail($"JobHandlerで扱えないアクション: {job.Action}");
        }
    }
}
