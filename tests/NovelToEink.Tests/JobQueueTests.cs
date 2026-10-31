using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NovelToEink.Core;
using Xunit;

namespace NovelToEink.Tests;

/// <summary>テスト用の fake clock（TimeProvider）。</summary>
internal sealed class FakeClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan t) => _now += t;
}

/// <summary>注入可能なスタブハンドラー。OpenCodeレビュー案（§6）準拠。</summary>
internal sealed class StubJobHandler : IJobHandler
{
    public Func<JobRecord, CancellationToken, Task<JobResult>>? Next { get; set; }
    public int Active { get; private set; }
    public int MaxActive { get; private set; }

    public async Task<JobResult> ExecuteAsync(JobRecord job, CancellationToken ct)
    {
        Active++;
        if (Active > MaxActive) MaxActive = Active;
        try
        {
            if (Next is null) return JobResult.Ok();
            return await Next(job, ct);
        }
        finally { Active--; }
    }
}

public class JobQueueTests
{
    private static JobQueue MakeQueue(TempDir dir, IJobHandler handler, FakeClock clock,
        Action<TimeSpan>? recordWait = null, int maxRetries = 3, int minJobIntervalMs = 0)
    {
        Func<TimeSpan, CancellationToken, Task> delay = (t, ct) =>
        {
            recordWait?.Invoke(t);
            return Task.CompletedTask;
        };
        return new JobQueue(Path.Combine(dir.Path, "jobs.json"), handler, clock, delay, maxRetries, minJobIntervalMs);
    }

    [Fact]
    public async Task Enqueue_ProcessNext_FifoOrder()
    {
        using var dir = new TempDir();
        var order = new List<string>();
        var handler = new StubJobHandler();
        handler.Next = (j, ct) => { order.Add(j.WorkId); return Task.FromResult(JobResult.Ok()); };
        var q = MakeQueue(dir, handler, new FakeClock());

        q.Load();
        q.Enqueue("a", JobAction.Download);
        q.Enqueue("b", JobAction.Download);

        await q.ProcessNextAsync(CancellationToken.None);
        await q.ProcessNextAsync(CancellationToken.None);

        Assert.Equal(new[] { "a", "b" }, order);
        Assert.All(q.Jobs, j => Assert.Equal(JobState.Done, j.State));
    }

    [Fact]
    public void DuplicateEnqueue_IsSkipped()
    {
        using var dir = new TempDir();
        var q = MakeQueue(dir, new StubJobHandler(), new FakeClock());
        q.Load();

        var first = q.Enqueue("a", JobAction.Download);
        var dup = q.Enqueue("a", JobAction.Download);

        Assert.NotNull(first);
        Assert.Null(dup);
        Assert.Single(q.Jobs);
    }

    [Fact]
    public async Task HandlerThrows_RetriesThenFailsAtLimit()
    {
        using var dir = new TempDir();
        var clock = new FakeClock();
        int attempts = 0;
        var handler = new StubJobHandler();
        handler.Next = (j, ct) =>
        {
            attempts++;
            clock.Advance(TimeSpan.FromMilliseconds(10));
            throw new InvalidOperationException("boom");
        };
        var q = MakeQueue(dir, handler, clock, maxRetries: 3);
        q.Load();
        var job = q.Enqueue("a", JobAction.Download)!;

        for (int i = 0; i < 4; i++) await q.ProcessNextAsync(CancellationToken.None);

        Assert.Equal(4, attempts);          // 1 + retry 3
        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal(3, job.RetryCount);
    }

    [Fact]
    public async Task Cancellation_BecomesCancelled_NotFailed_AndNoRetry()
    {
        using var dir = new TempDir();
        var cts = new CancellationTokenSource();
        var handler = new StubJobHandler();
        handler.Next = async (j, ct) =>
        {
            // 実行中に外部からキャンセル
            cts.Cancel();
            try { await Task.Delay(50, ct); }
            catch (OperationCanceledException) { return JobResult.Fail("cancelled"); }
            return JobResult.Ok();
        };
        var q = MakeQueue(dir, handler, new FakeClock());
        q.Load();
        var job = q.Enqueue("a", JobAction.Download)!;

        await q.ProcessNextAsync(cts.Token);

        Assert.Equal(JobState.Cancelled, job.State);
        Assert.Equal(0, job.RetryCount);
    }

    [Fact]
    public async Task OneFailure_DoesNotBlockOthers()
    {
        using var dir = new TempDir();
        var handler = new StubJobHandler();
        handler.Next = (j, ct) => j.WorkId == "bad"
            ? Task.FromResult(JobResult.Fail("error"))
            : Task.FromResult(JobResult.Ok());
        var q = MakeQueue(dir, handler, new FakeClock(), maxRetries: 1);
        q.Load();
        var bad = q.Enqueue("bad", JobAction.Download)!;
        var good = q.Enqueue("good", JobAction.Download)!;

        await q.ProcessNextAsync(CancellationToken.None); // bad 1回目: 失敗→再キュー
        await q.ProcessNextAsync(CancellationToken.None); // bad 2回目: retry limit 到達→Failed
        await q.ProcessNextAsync(CancellationToken.None); // good: 止まらず実行される

        Assert.Equal(JobState.Failed, bad.State);
        Assert.Equal(JobState.Done, good.State);
    }

    [Fact]
    public async Task MaxConcurrency_IsAlwaysOne()
    {
        using var dir = new TempDir();
        var handler = new StubJobHandler();
        handler.Next = async (j, ct) => { await Task.Delay(30, ct); return JobResult.Ok(); };
        var q = MakeQueue(dir, handler, new FakeClock());
        q.Load();
        for (int i = 0; i < 3; i++) q.Enqueue($"w{i}", JobAction.Download);

        var cts = new CancellationTokenSource();
        await Task.Run(() => q.RunLoopAsync(cts.Token));

        Assert.Equal(1, handler.MaxActive);
        Assert.Equal(3, q.Jobs.Count(j => j.State == JobState.Done));
    }

    [Fact]
    public async Task MinInterval_AppliedBetweenJobs()
    {
        using var dir = new TempDir();
        var waits = new List<TimeSpan>();
        var handler = new StubJobHandler(); // 実行時間 0（クロック進めず）
        var q = MakeQueue(dir, handler, new FakeClock(), recordWait: t => waits.Add(t), minJobIntervalMs: 50);
        q.Load();
        q.Enqueue("a", JobAction.Download);
        q.Enqueue("b", JobAction.Download);

        await q.ProcessNextAsync(CancellationToken.None); // 1件目: 直前実行なし → wait 0（記録されないか 0）
        await q.ProcessNextAsync(CancellationToken.None); // 2件目: 50ms 要求される

        Assert.Contains(TimeSpan.FromMilliseconds(50), waits);
    }

    [Fact]
    public void ParallelEnqueue_PersistStaysConsistent()
    {
        using var dir = new TempDir();
        var q = MakeQueue(dir, new StubJobHandler(), new FakeClock());
        q.Load();

        Parallel.For(0, 10, i => q.Enqueue($"w{i}", JobAction.Check));

        var json = File.ReadAllText(Path.Combine(dir.Path, "jobs.json"));
        var records = System.Text.Json.JsonSerializer.Deserialize<List<JobRecord>>(json);
        Assert.Equal(10, records!.Count);
    }

    [Fact]
    public void CorruptJobsFile_IsQuarantinedAsBad()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "jobs.json");
        File.WriteAllText(path, "{ not valid json ");

        var q = MakeQueue(dir, new StubJobHandler(), new FakeClock());
        q.Load();

        Assert.False(File.Exists(path));
        Assert.True(File.Exists(path + ".bad"));
        Assert.Empty(q.Jobs);
    }

    [Fact]
    public void Load_RepairsRunningToInterrupted()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "jobs.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new List<JobRecord>
        {
            new() { WorkId = "a", Action = JobAction.Download, State = JobState.Running }
        }));

        var q = MakeQueue(dir, new StubJobHandler(), new FakeClock());
        q.Load();

        Assert.Equal(JobState.Interrupted, q.Jobs[0].State);
    }

    [Fact]
    public void Cancel_QueuedJob_IsRemoved()
    {
        using var dir = new TempDir();
        var q = MakeQueue(dir, new StubJobHandler(), new FakeClock());
        q.Load();
        var job = q.Enqueue("a", JobAction.Download)!;

        Assert.True(q.Cancel(job.Id));
        Assert.Empty(q.Jobs);
    }

    // --- OpenCode post-impl check の P1/coverage gap に対するテスト ---

    [Fact]
    public void Jobs_ReturnsSnapshot_NotLiveList()
    {
        using var dir = new TempDir();
        var q = MakeQueue(dir, new StubJobHandler(), new FakeClock());
        q.Load();
        q.Enqueue("a", JobAction.Download);

        var snap = q.Jobs;                 // スナップショット（UI bind 用）
        q.Enqueue("b", JobAction.Download); // 労働側からの変更は snapshot に反映されない

        Assert.Single(snap);                // スナップショットは1件のまま
        Assert.Equal(2, q.Jobs.Count);
    }

    [Fact]
    public void Raise_HandlerThrows_QueueSurvives()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "jobs.json");
        var q = MakeQueue(dir, new StubJobHandler(), new FakeClock());
        q.Load();
        q.StateChanged += _ => throw new InvalidOperationException("UI handler boom");
        var job = q.Enqueue("a", JobAction.Download)!;   // 例外を飲み込まずEnqueueを壊さない
        Assert.NotNull(job);
        Assert.Single(q.Jobs);
    }

    [Fact]
    public async Task ConcurrentProcessNext_IsRejected()
    {
        using var dir = new TempDir();
        var handler = new StubJobHandler();
        handler.Next = async (j, ct) => { await Task.Delay(30, ct); return JobResult.Ok(); };
        var q = MakeQueue(dir, handler, new FakeClock());
        q.Load();
        q.Enqueue("a", JobAction.Download);

        var first = q.ProcessNextAsync(CancellationToken.None);
        // async メソッドの throw は同期スローではなく返Taskに捕捉される → ThrowsAsync
        var second = q.ProcessNextAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => second);
        await first;
        Assert.Equal(JobState.Done, q.Jobs[0].State);
    }

    [Fact]
    public async Task CancelDuringWait_KeepsLoopRunning()
    {
        using var dir = new TempDir();
        var handler = new StubJobHandler();
        // 実delayを使う（race を実時間で再現）
        var order = new List<string>();
        handler.Next = (j, ct) => { lock (order) order.Add(j.WorkId); return Task.FromResult(JobResult.Ok()); };
        var q = new JobQueue(Path.Combine(dir.Path, "jobs.json"), handler, new FakeClock(),
            Task.Delay, maxRetries: 3, minJobIntervalMs: 150);
        q.Load();
        // 最初のジョブは直前完了が無いので待たずに走る（wait=0）。よって dummy を先頭に置いて
        // a が実際の wait（150ms） window に入るようにする
        q.Enqueue("prev", JobAction.Download);
        var a = q.Enqueue("a", JobAction.Download)!;
        var b = q.Enqueue("b", JobAction.Download)!;

        var loop = Task.Run(() => q.RunLoopAsync(CancellationToken.None));
        await Task.Delay(50);              // prev 処理済み、loop は a の wait（150ms）中
        q.Cancel(a.Id);                    // wait中に cancel → worker は止まらず b を処理すべき
        await Task.WhenAny(loop, Task.Delay(5000));

        Assert.True(loop.IsCompleted);
        Assert.Equal(JobState.Done, b.State);      // 旧実装: race で loop が break し b は Queued のまま
        Assert.Equal(new[] { "prev", "b" }, order); // 旧実装: cancel 済み a を処理してしまう
        Assert.DoesNotContain("a", q.Jobs.Select(j => j.WorkId));
    }

    [Fact]
    public void ExistingBadFile_IsOverwrittenByNewQuarantine()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "jobs.json");
        File.WriteAllText(path + ".bad", "old");
        File.WriteAllText(path, "{ not valid json ");

        var q = MakeQueue(dir, new StubJobHandler(), new FakeClock());
        q.Load();  // .bad 既存でも退避が成功し、壊れ内容は新しい .bad に残る

        Assert.True(File.Exists(path + ".bad"));
        Assert.Equal("{ not valid json ", File.ReadAllText(path + ".bad"));
        Assert.Empty(q.Jobs);
    }

    [Fact]
    public async Task Cancel_RunningJob_ObservesToken_BecomesCancelled()
    {
        using var dir = new TempDir();
        var handler = new StubJobHandler();
        handler.Next = async (j, ct) => { await Task.Delay(100, ct); return JobResult.Ok(); };
        var q = MakeQueue(dir, handler, new FakeClock());
        q.Load();
        var job = q.Enqueue("a", JobAction.Download)!;

        var t = q.ProcessNextAsync(CancellationToken.None);
        await Task.Delay(20);              // handler 実行中
        q.Cancel(job.Id);                  // linked CTS 経由（q.Cancel() の Running 路径）
        await t;
        Assert.Equal(JobState.Cancelled, job.State);
    }

    [Fact]
    public void DuplicateEnqueue_SameWorkIdDifferentUrl_NotDeduped()
    {
        using var dir = new TempDir();
        var q = MakeQueue(dir, new StubJobHandler(), new FakeClock());
        q.Load();

        // 別作品（Url違い）は WorkId が同じでも重複扱いしない（空WorkIdのエントリ対策）。
        var first = q.Enqueue("a", JobAction.Download, "https://x/a", NovelSite.Syosetu);
        var second = q.Enqueue("a", JobAction.Download, "https://x/b", NovelSite.Syosetu);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(2, q.Jobs.Count);
    }

    [Fact]
    public async Task CurrentJob_ReflectsRunningJob_ThenNull()
    {
        using var dir = new TempDir();
        var handler = new StubJobHandler();
        var q = MakeQueue(dir, handler, new FakeClock());
        handler.Next = (j, ct) =>
        {
            // 実行中の CurrentJob は実行中のジョブそのもの（ViewModelのCancel/表示用）。
            Assert.Same(j, q.CurrentJob);
            return Task.FromResult(JobResult.Ok());
        };
        q.Load();
        var job = q.Enqueue("a", JobAction.Download)!;
        Assert.Null(q.CurrentJob);            // 実行前は null

        await q.ProcessNextAsync(CancellationToken.None);
        Assert.Null(q.CurrentJob);            // 完了後は null
    }

    [Fact]
    public void SiteFromUrl_MapsKakuyomuAndSyosetu()
    {
        // ViewModelのenqueueがURLからSiteを決める（キャッシュパス解決用）。
        Assert.Equal(NovelSite.Kakuyomu, JobQueue.SiteFromUrl("https://kakuyomu.jp/works/1111111111111111111"));
        Assert.Equal(NovelSite.Syosetu, JobQueue.SiteFromUrl("https://ncode.syosetu.com/n1234ab/"));
        Assert.Equal(NovelSite.Syosetu, JobQueue.SiteFromUrl("https://novel18.syosetu.com/n1234ab/"));
    }

    [Fact]
    public void ClearQueue_MarksRemovedJobsCancelled()
    {
        using var dir = new TempDir();
        var q = MakeQueue(dir, new StubJobHandler(), new FakeClock());
        q.Load();
        var job = q.Enqueue("a", JobAction.Download)!;
        q.ClearQueue();

        // ClearQueueは除去前にCancelled化すべき（ иначе ProcessNextAsyncの
        // 1500ms待機後のQueued再チェックをすり抜けて実行開始する — レビューC-3）。
        Assert.Equal(JobState.Cancelled, job.State);
        Assert.Empty(q.Jobs);
    }

    [Fact]
    public void Enqueue_PersistFailure_IsSwallowed()
    {
        using var dir = new TempDir();
        var blocker = Path.Combine(dir.Path, "blocker.txt");
        File.WriteAllText(blocker, "x");

        // 親がファイル → Directory.CreateDirectoryがthrow。persist I/O失敗は
        // 飲まねばならない（レビューM-3: UIクリックハンドラ直下でクラッシュ防止）。
        var q = new JobQueue(Path.Combine(blocker, "jobs.json"), new StubJobHandler(),
            new FakeClock(), (t, ct) => Task.CompletedTask, 3, 0);
        var job = q.Enqueue("a", JobAction.Download);
        Assert.NotNull(job);
    }

    [Fact]
    public async Task ClearQueueDuringWait_SkipsClearedJob_AndLoopContinues()
    {
        using var dir = new TempDir();
        var handler = new StubJobHandler();
        var order = new List<string>();
        handler.Next = (j, ct) => { lock (order) order.Add(j.WorkId); return Task.FromResult(JobResult.Ok()); };

        // タイミングでなくシグナルで「wait window 中」を確定させる（CI runner の
        // スケジュール遅延で Task.Delay(50) が window 後に届くと flaky になる）。
        // FakeClock は進まないため window は full（1000ms） guarantees。
        var windowStarted = new TaskCompletionSource();
        int delayCalls = 0;
        Func<TimeSpan, CancellationToken, Task> delay = async (w, ct) =>
        {
            delayCalls++;
            if (delayCalls == 1) windowStarted.TrySetResult(); // prev は window なし → 1回目が a の window
            await Task.Delay(w, ct);
        };
        var q = new JobQueue(Path.Combine(dir.Path, "jobs.json"), handler, new FakeClock(),
            delay, maxRetries: 3, minJobIntervalMs: 1000);
        q.Load();
        q.Enqueue("prev", JobAction.Download);   // FakeClock 下 elapsed 巨大 → wait 0 で即実行
        var a = q.Enqueue("a", JobAction.Download)!;
        var b = q.Enqueue("b", JobAction.Download)!;

        var loop = Task.Run(() => q.RunLoopAsync(CancellationToken.None));
        await windowStarted.Task;                // a の wait window に入ったことが確定
        int cleared = q.ClearQueue();            // wait中に ClearQueue → Cancelled化语义（C-3修正）
        await Task.WhenAny(loop, Task.Delay(5000));

        Assert.Equal(2, cleared);
        Assert.Equal(JobState.Cancelled, a.State);
        Assert.Equal(JobState.Cancelled, b.State);
        Assert.Equal(new[] { "prev" }, order);   // 旧実装: a（Cancelled化されていない）を処理してしまう
    }
}
