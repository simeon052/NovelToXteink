using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NovelToEink.Core;

using Xunit;

namespace NovelToEink.Tests;

/// <summary>ネットワークを叩かない fake scraper（呼び出し回数を数える）。IGetScraper seam 用。</summary>
internal sealed class FakeScraper : INovelScraper, IGetScraper
{
    public NovelMetadata Meta = new()
    {
        Site = NovelSite.Syosetu, WorkId = "w1", Title = "ネットタイトル",
        Author = "著", Description = "説明", Url = "https://ncode.syosetu.com/w1/",
    };
    public List<EpisodeRef> Toc = [new() { Index = 1, Title = "第1話", Url = "https://ncode.syosetu.com/w1/1" }];
    public int GetScraperCalls;
    public int NetworkCalls;

    public NovelSite Site => NovelSite.Syosetu;
    public bool CanHandle(string url) => url.Contains("fake");
    public (NovelSite site, string workId)? TryParseWorkId(string url) => (NovelSite.Syosetu, "w1");

    public INovelScraper GetScraper(string url)
    {
        GetScraperCalls++;
        return this;
    }

    public Task<NovelMetadata> GetMetadataAsync(string url, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); // handler は ct を観察する——fake も観察して再現
        NetworkCalls++;
        return Task.FromResult(Meta);
    }

    public Task<IReadOnlyList<EpisodeRef>> GetTableOfContentsAsync(string url, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        NetworkCalls++;
        return Task.FromResult((IReadOnlyList<EpisodeRef>)Toc);
    }

    public Task<EpisodeContent> GetEpisodeAsync(EpisodeRef episode, EpubOptions options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        NetworkCalls++;
        return Task.FromResult(new EpisodeContent
        {
            Ref = episode,
            BodyHtml = "<p>本文</p>",
        });
    }
}

public class JobHandlerTests
{
    private static AppSettings MakeSettings(TempDir dir)
    {
        var s = new AppSettings { OutputFolder = dir.Path, RequestDelayMs = 0 };
        s.EnableProofreading = false;
        s.GenerateXtc = false;
        return s;
    }

    // ---- JobRecord: URL とサイトは永続化されねばならない（handler が使う）----

    [Fact]
    public void JobRecord_PersistsUrlAndSite()
    {
        var job = new JobRecord { WorkId = "w1", Action = JobAction.Check, Url = "https://fake.example/w1", Site = NovelSite.Kakuyomu };
        var json = System.Text.Json.JsonSerializer.Serialize(job);
        var back = System.Text.Json.JsonSerializer.Deserialize<JobRecord>(json)!;
        Assert.Equal("https://fake.example/w1", back.Url);
        Assert.Equal(NovelSite.Kakuyomu, back.Site);
    }

    [Fact]
    public void Enqueue_StoresUrlAndSite()
    {
        using var dir = new TempDir();
        var q = new JobQueue(Path.Combine(dir.Path, "jobs.json"), new StubJobHandler(), new FakeClock());
        q.Load();
        var job = q.Enqueue("w1", JobAction.Check, "https://fake.example/w1", NovelSite.Syosetu)!;
        Assert.Equal("https://fake.example/w1", job.Url);
        Assert.Equal(NovelSite.Syosetu, job.Site);
    }

    // ---- Check: キャッシュヒットでネットワークを叩かない ----

    [Fact]
    public async Task Check_CacheHit_DoesNotTouchNetwork()
    {
        using var dir = new TempDir();
        var settings = MakeSettings(dir);
        var fake = new FakeScraper();
        var library = new LibraryService(settings, fake);

        // cache seed: 2話キャッシュ、エントリは1話→差分を検知
        var cache = new DownloadCache(DownloadCache.GetCacheRoot(dir.Path), "Syosetu", "w1");
        cache.SetMetadata(new NovelMetadata
        {
            Site = NovelSite.Syosetu, WorkId = "w1", Title = "キャッシュ題名", Author = "著",
            Description = "", Url = "https://fake.example/w1", IsCompleted = false,
            SiteLastUpdated = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
        });
        cache.SetEpisode(new EpisodeContent { Ref = new EpisodeRef { Index = 1, Title = "第1話", Url = "https://fake.example/w1/1" }, BodyHtml = "<p>a</p>" });
        cache.SetEpisode(new EpisodeContent { Ref = new EpisodeRef { Index = 2, Title = "第2話", Url = "https://fake.example/w1/2" }, BodyHtml = "<p>b</p>" });

        var entry = library.Store.CreateEntry("https://fake.example/w1");
        entry.EpisodeCount = 1;
        entry.LastEpisodeTitle = "第1話";

        var handler = new JobHandler(library, fake);
        var job = new JobRecord { WorkId = "w1", Action = JobAction.Check, Url = "https://fake.example/w1", Site = NovelSite.Syosetu };
        var result = await handler.ExecuteAsync(job, CancellationToken.None);

        Assert.True(result.IsOk);
        Assert.Equal(0, fake.NetworkCalls);          // cache-first: ネットワーク不使用
        Assert.Equal(UpdateStatus.UpdateAvailable, entry.Status); // 1話→2話の差分
        Assert.Equal("キャッシュ題名", entry.Title);
    }

    [Fact]
    public async Task Check_CacheHit_ReportsCacheUsage()
    {
        using var dir = new TempDir();
        var settings = MakeSettings(dir);
        var fake = new FakeScraper();
        var library = new LibraryService(settings, fake);

        var cache = new DownloadCache(DownloadCache.GetCacheRoot(dir.Path), "Syosetu", "w1");
        cache.SetMetadata(new NovelMetadata { Site = NovelSite.Syosetu, WorkId = "w1", Title = "t", Url = "https://fake.example/w1" });
        cache.SetEpisode(new EpisodeContent { Ref = new EpisodeRef { Index = 1, Title = "第1話", Url = "https://fake.example/w1/1" }, BodyHtml = "<p>a</p>" });

        var entry = library.Store.CreateEntry("https://fake.example/w1");
        var messages = new List<string>();
        var handler = new JobHandler(library, fake, null, new ProgressRecorder(messages));
        var job = new JobRecord { WorkId = "w1", Action = JobAction.Check, Url = "https://fake.example/w1", Site = NovelSite.Syosetu };
        await handler.ExecuteAsync(job, CancellationToken.None);

        Assert.Contains(messages, m => m.Contains("キャッシュ"));
    }

    [Fact]
    public async Task Check_CacheMiss_UsesScraper()
    {
        using var dir = new TempDir();
        var settings = MakeSettings(dir);
        var fake = new FakeScraper();
        var library = new LibraryService(settings, fake);
        var entry = library.Store.CreateEntry("https://fake.example/w1");

        var handler = new JobHandler(library, fake);
        var job = new JobRecord { WorkId = "w1", Action = JobAction.Check, Url = "https://fake.example/w1", Site = NovelSite.Syosetu };
        var result = await handler.ExecuteAsync(job, CancellationToken.None);

        Assert.True(result.IsOk);
        Assert.True(fake.NetworkCalls > 0);
        Assert.Equal("ネットタイトル", entry.Title);
    }

    // ---- Convert: cache-only（ネットワーク不使用）。miss はキャッシュディレクトリを作らない ----

    [Fact]
    public async Task Convert_CacheOnly_SucceedsWithoutNetwork()
    {
        using var dir = new TempDir();
        var settings = MakeSettings(dir);
        var fake = new FakeScraper();
        var library = new LibraryService(settings, fake);

        var cache = new DownloadCache(DownloadCache.GetCacheRoot(dir.Path), "Syosetu", "w1");
        cache.SetMetadata(new NovelMetadata
        {
            Site = NovelSite.Syosetu, WorkId = "w1", Title = "キャッシュ題名", Author = "著",
            Description = "", Url = "https://fake.example/w1",
        });
        cache.SetEpisode(new EpisodeContent { Ref = new EpisodeRef { Index = 1, Title = "第1話", Url = "https://fake.example/w1/1" }, BodyHtml = "<p>a</p>" });
        cache.SetImage("https://fake.example/cover.jpg", MakePng());

        var handler = new JobHandler(library, fake);
        var job = new JobRecord { WorkId = "w1", Action = JobAction.Convert, Url = "https://fake.example/w1", Site = NovelSite.Syosetu };
        var result = await handler.ExecuteAsync(job, CancellationToken.None);

        Assert.True(result.IsOk);
        Assert.Equal(0, fake.NetworkCalls);
        var entry = Assert.Single(library.Entries);
        Assert.Equal("キャッシュ題名", entry.Title);
        Assert.Equal(1, entry.EpisodeCount);
        Assert.True(entry.CoverPending);              // AutoCover既定 true → 暫定表紙
        Assert.True(entry.EpubParts.Any(File.Exists));
    }

    [Fact]
    public async Task Convert_CacheMiss_Fails_WithoutCreatingCacheDir()
    {
        using var dir = new TempDir();
        var settings = MakeSettings(dir);
        var fake = new FakeScraper();
        var library = new LibraryService(settings, fake);

        var handler = new JobHandler(library, fake);
        var job = new JobRecord { WorkId = "w9", Action = JobAction.Convert, Url = "https://fake.example/w9", Site = NovelSite.Syosetu };
        var result = await handler.ExecuteAsync(job, CancellationToken.None);

        Assert.False(result.IsOk);
        // cache-onlyConvert はキャッシュディレクトリを作成してはならない
        Assert.False(Directory.Exists(DownloadCache.GetWorkCacheDir(DownloadCache.GetCacheRoot(dir.Path), "Syosetu", "w9")));
    }

    [Fact]
    public async Task Convert_CacheHit_CallsResolver_WhenAutoCoverOff()
    {
        using var dir = new TempDir();
        var settings = MakeSettings(dir);
        settings.AutoCover = false;
        var fake = new FakeScraper();
        var library = new LibraryService(settings, fake);

        var cache = new DownloadCache(DownloadCache.GetCacheRoot(dir.Path), "Syosetu", "w1");
        cache.SetMetadata(new NovelMetadata { Site = NovelSite.Syosetu, WorkId = "w1", Title = "t", Url = "https://fake.example/w1" });
        cache.SetEpisode(new EpisodeContent { Ref = new EpisodeRef { Index = 1, Title = "第1話", Url = "https://fake.example/w1/1" }, BodyHtml = "<p>a</p>" });
        cache.SetImage("https://fake.example/cover.jpg", MakePng());

        var resolverCalled = false;
        var handler = new JobHandler(library, fake, (job, candidates) =>
        {
            resolverCalled = true;
            return candidates.First(); // resolver は candidates を受け取る
        });
        var job = new JobRecord { WorkId = "w1", Action = JobAction.Convert, Url = "https://fake.example/w1", Site = NovelSite.Syosetu };
        var result = await handler.ExecuteAsync(job, CancellationToken.None);

        Assert.True(result.IsOk);
        Assert.True(resolverCalled);
        var entry = Assert.Single(library.Entries);
        Assert.False(entry.CoverPending);
    }

    // ---- handler は ct を観察する（OpenCode §6）----

    [Fact]
    public async Task Handler_ObservesCancellation()
    {
        using var dir = new TempDir();
        var settings = MakeSettings(dir);
        var fake = new FakeScraper();
        var library = new LibraryService(settings, fake);
        var handler = new JobHandler(library, fake);
        var job = new JobRecord { WorkId = "w1", Action = JobAction.Check, Url = "https://fake.example/w1", Site = NovelSite.Syosetu };

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => handler.ExecuteAsync(job, cts.Token));
        Assert.Equal(0, fake.NetworkCalls); // 観察済み: cancel 後にネットワークへ進まない
    }

    // ---- Download job は seam 経由で fake を使う ----

    [Fact]
    public async Task Download_UsesInjectedScraper()
    {
        using var dir = new TempDir();
        var settings = MakeSettings(dir);
        var fake = new FakeScraper();
        var library = new LibraryService(settings, fake);

        var handler = new JobHandler(library, fake);
        var job = new JobRecord { WorkId = "w1", Action = JobAction.Download, Url = "https://fake.example/w1", Site = NovelSite.Syosetu };
        var result = await handler.ExecuteAsync(job, CancellationToken.None);

        Assert.True(result.IsOk);
        Assert.True(fake.NetworkCalls > 0);
        Assert.Single(library.Entries);
    }

    private static byte[] MakePng()
    {
        // 1x1 PNG（ImageProcessor.TryLoad に読ませる実バイト列）
        return Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYGBgAAAABQABpfZFQAAAAABJRU5ErkJggg==");
    }

    private sealed class ProgressRecorder : IProgress<string>
    {
        private readonly List<string> _list;
        public ProgressRecorder(List<string> list) => _list = list;
        public void Report(string value) => _list.Add(value);
    }
}
