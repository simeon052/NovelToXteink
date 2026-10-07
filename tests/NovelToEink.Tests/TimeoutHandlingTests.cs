using NovelToEink.Core;

namespace NovelToEink.Tests;

/// <summary>
/// HTTP のタイムアウトは TaskCanceledException（OperationCanceledException の子）で届く。
/// 「利用者の中断」と「タイムアウト」を取り違えると、画像 1 枚・API 1 回の遅延が処理全体の失敗になる。
/// 本物の HttpClient のタイムアウトを通して確かめる（回帰テスト）。
/// </summary>
public class TimeoutHandlingTests
{
    private const string Url = "https://example.test/img.png";

    // ---- NovelDownloadService.TryDownloadImageAsync (画像 1 枚の取得) ----

    [Fact]
    public async Task Image_TimeoutIsASoftFailure_ReturnsNull_InsteadOfAbortingTheDownload()
    {
        using var fetcher = FakeHttp.Fetcher(FakeHttpHandler.Hangs());
        using var svc = new NovelDownloadService(fetcher);

        var image = await svc.TryDownloadImageAsync(Url, 1, false, CancellationToken.None);

        Assert.Null(image);   // 画像 1 枚の失敗は致命ではない。例外にして小説全体を止めてはいけない
    }

    [Fact]
    public async Task Image_UserCancellation_StillPropagates()
    {
        using var fetcher = FakeHttp.Fetcher(FakeHttpHandler.Hangs(), timeoutMs: 60_000);
        using var svc = new NovelDownloadService(fetcher);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => svc.TryDownloadImageAsync(Url, 1, false, cts.Token));
    }

    [Fact]
    public async Task Image_ValidImage_IsReturned()
    {
        using var fetcher = FakeHttp.Fetcher(FakeHttpHandler.Returns(FakeHttp.TinyPng));
        using var svc = new NovelDownloadService(fetcher);

        var image = await svc.TryDownloadImageAsync(Url, 3, false, CancellationToken.None);

        Assert.NotNull(image);
        Assert.Equal(1, image!.Width);
        Assert.Equal(3, image.EpisodeIndex);
        Assert.Equal(Url, image.SourceUrl);
    }

    [Fact]
    public async Task Image_NotAnImage_ReturnsNull()
    {
        using var fetcher = FakeHttp.Fetcher(FakeHttpHandler.Returns("<html>404</html>"));
        using var svc = new NovelDownloadService(fetcher);

        Assert.Null(await svc.TryDownloadImageAsync(Url, 1, false, CancellationToken.None));
    }

    // ---- SyosetuScraper.FetchNarouStatusAsync (完結状態・最終更新日) ----

    [Fact]
    public async Task NarouStatus_Timeout_FallsBackToNotCompleted_InsteadOfFailingMetadata()
    {
        using var fetcher = FakeHttp.Fetcher(FakeHttpHandler.Hangs());
        var scraper = new SyosetuScraper(fetcher);

        var (completed, lastUp) = await scraper.FetchNarouStatusAsync("ncode", "n1234ab", CancellationToken.None);

        Assert.False(completed);
        Assert.Null(lastUp);
    }

    [Fact]
    public async Task NarouStatus_UserCancellation_StillPropagates()
    {
        using var fetcher = FakeHttp.Fetcher(FakeHttpHandler.Hangs(), timeoutMs: 60_000);
        var scraper = new SyosetuScraper(fetcher);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scraper.FetchNarouStatusAsync("ncode", "n1234ab", cts.Token));
    }

    [Fact]
    public async Task NarouStatus_CompletedWork_ReadsEndAndLastUpdateAsJst()
    {
        const string api = "[{\"allcount\":1},{\"end\":0,\"general_lastup\":\"2026-01-02 03:04:05\"}]";
        using var fetcher = FakeHttp.Fetcher(FakeHttpHandler.Returns(api));
        var scraper = new SyosetuScraper(fetcher);

        var (completed, lastUp) = await scraper.FetchNarouStatusAsync("ncode", "n1234ab", CancellationToken.None);

        Assert.True(completed);                                   // end == 0 は完結済み／短編
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(9)), lastUp);
    }

    [Fact]
    public async Task NarouStatus_OngoingWork_IsNotCompleted()
    {
        const string api = "[{\"allcount\":1},{\"end\":1,\"general_lastup\":\"2026-01-02 03:04:05\"}]";
        using var fetcher = FakeHttp.Fetcher(FakeHttpHandler.Returns(api));
        var scraper = new SyosetuScraper(fetcher);

        var (completed, _) = await scraper.FetchNarouStatusAsync("ncode", "n1234ab", CancellationToken.None);

        Assert.False(completed);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{\"end\":0}")]
    public async Task NarouStatus_UnexpectedResponse_FallsBackToNotCompleted(string api)
    {
        using var fetcher = FakeHttp.Fetcher(FakeHttpHandler.Returns(api));
        var scraper = new SyosetuScraper(fetcher);

        var (completed, lastUp) = await scraper.FetchNarouStatusAsync("ncode", "n1234ab", CancellationToken.None);

        Assert.False(completed);
        Assert.Null(lastUp);
    }

    [Theory]
    [InlineData("ncode", "novelapi")]
    [InlineData("novel18", "novel18api")]
    public async Task NarouStatus_UsesTheApiHostMatchingTheSite(string host, string expectedApiHost)
    {
        string? requested = null;
        var handler = new FakeHttpHandler((req, _) =>
        {
            requested = req.RequestUri!.ToString();
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent("[{},{\"end\":1}]") });
        });
        using var fetcher = FakeHttp.Fetcher(handler);

        await new SyosetuScraper(fetcher).FetchNarouStatusAsync(host, "n1234ab", CancellationToken.None);

        Assert.Contains($"api.syosetu.com/{expectedApiHost}/api/", requested);
        Assert.Contains("ncode=n1234ab", requested);
    }
}
