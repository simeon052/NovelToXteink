using System.IO;
using System.Text.Json;

namespace NovelToEink.Core;

/// <summary>
/// 単一FIFOワーカーでジョブを直列処理するキュー。
/// - 多重 enqueue は WorkId+Action の重複（Queued/Running）で dedup。
/// - 失敗は RetryCount++ して maxRetries まで再キュー、超限で Failed。
/// - キャンセルは Cancelled（retry 計上なし）。1件の失敗で全体が止まらない。
/// - jobs.json は原子的置換（temp → Move）。読み込み時 Running→Interrupted、
///   パース失敗は .bad へ退避して空で起動（DataLossRegressionTests の文化に準拠）。
/// </summary>
public sealed class JobQueue
{
    private readonly string _path;
    private readonly IJobHandler _handler;
    private readonly TimeProvider _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly int _maxRetries;
    private readonly int _minJobIntervalMs;
    private readonly object _lock = new();
    private List<JobRecord> _jobs = new();
    private DateTimeOffset _lastFinish = DateTimeOffset.MinValue;
    private JobRecord? _currentJob;
    private CancellationTokenSource? _currentCts;
    private bool _processing;

    public event Action<JobRecord>? StateChanged;

    /// <summary>実行中のジョブ（UI表示・Cancel用）。非実行中は null。</summary>
    public JobRecord? CurrentJob
    {
        get { lock (_lock) return _currentJob; }
    }

    /// <summary>ロック下でのスナップショット。呼び出し以降の更新は反映されない（UI bind 用）。
    /// 変更は StateChanged イベントで通知される。</summary>
    public IReadOnlyList<JobRecord> Jobs
    {
        get { lock (_lock) return _jobs.ToList(); }
    }

    /// <summary>URLからサイトを決める（enqueue時のSite解決・キャッシュパス用）。</summary>
    public static NovelSite SiteFromUrl(string url)
        => url.Contains("kakuyomu", StringComparison.OrdinalIgnoreCase)
            ? NovelSite.Kakuyomu : NovelSite.Syosetu;

    public JobQueue(string jobsFilePath, IJobHandler handler, TimeProvider clock,
        Func<TimeSpan, CancellationToken, Task>? delay = null, int maxRetries = 3, int minJobIntervalMs = 1500)
    {
        _path = jobsFilePath;
        _handler = handler;
        _clock = clock;
        _delay = delay ?? Task.Delay;
        _maxRetries = maxRetries;
        _minJobIntervalMs = minJobIntervalMs;
    }

    /// <summary>jobs.json を読み込み、クラッシュ由来の Running を Interrupted に修復する。</summary>
    public void Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_path)) return;
            List<JobRecord>? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<List<JobRecord>>(File.ReadAllText(_path));
            }
            catch (JsonException ex)
            {
                CoreLog.Warn("jobs.json が壊れています。.bad へ退避して空のキューで起動します", ex);
                try { File.Move(_path, _path + ".bad", overwrite: true); } catch (IOException iex) { CoreLog.Ignored(".bad 退避失敗", iex); }
                return;
            }
            if (parsed == null) return;
            foreach (var j in parsed)
            {
                if (j.State == JobState.Running)
                {
                    j.State = JobState.Interrupted;
                    j.FinishedAt = _clock.GetUtcNow();
                }
            }
            _jobs = parsed;
        }
    }

    /// <summary>enqueue。既存の Queued/Running と WorkId+Action が重複なら null を返す（dedup）。</summary>
    public JobRecord? Enqueue(string workId, JobAction action,
        string url = "", NovelSite site = NovelSite.Syosetu)
    {
        JobRecord job;
        lock (_lock)
        {
            var dup = _jobs.FirstOrDefault(j => j.WorkId == workId && j.Action == action
                && j.Url == url && (j.State == JobState.Queued || j.State == JobState.Running));
            if (dup != null) return null;

            job = new JobRecord { WorkId = workId, Action = action, Url = url, Site = site,
                EnqueuedAt = _clock.GetUtcNow() };
            _jobs.Add(job);
            PersistLocked();
        }
        Raise(job); // ロック外で発火（handler の reentrancy でデッドロックしない）
        return job;
    }

    /// <summary>次の Queued ジョブを1件処理して結果を返す。キューが空なら null。
    /// 同一インスタンスで並行実行中は InvalidOperationException（単一労働スレッド前提の enforcement）。</summary>
    public async Task<JobResult?> ProcessNextAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            if (_processing)
                throw new InvalidOperationException("ProcessNextAsync is already running on this queue (single worker per instance).");
            _processing = true;
        }
        try
        {
            while (true)
            {
                JobRecord job;
                TimeSpan wait;
                lock (_lock)
                {
                    var next = _jobs.FirstOrDefault(j => j.State == JobState.Queued);
                    if (next == null) return null;

                    var elapsed = (int)(_clock.GetUtcNow() - _lastFinish).TotalMilliseconds;
                    wait = TimeSpan.FromMilliseconds(Math.Max(0, _minJobIntervalMs - elapsed));
                    job = next;
                }

                if (wait > TimeSpan.Zero)
                {
                    try { await _delay(wait, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return null; } // 外側ctのキャンセル
                }

                CancellationTokenSource runCts;
                lock (_lock)
                {
                    // wait中にCancelされた件は処理せず再スキャン（loop を止めない。空判定とは区別）
                    if (job.State != JobState.Queued) continue;
                    job.State = JobState.Running;
                    job.StartedAt = _clock.GetUtcNow();
                    _currentJob = job;
                    _currentCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    runCts = _currentCts;
                    PersistLocked();
                }
                Raise(job); // ロック外で発火

                JobResult result;
                try
                {
                    result = await _handler.ExecuteAsync(job, runCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    result = JobResult.Fail("cancelled");
                }
                catch (Exception ex)
                {
                    result = JobResult.Fail(ex.Message);
                }

                lock (_lock)
                {
                    job.FinishedAt = _clock.GetUtcNow();
                    _lastFinish = job.FinishedAt.Value;
                    _currentJob = null;
                    _currentCts = null;

                    if (runCts.IsCancellationRequested)
                    {
                        job.State = JobState.Cancelled;   // キャンセルは Failed/Interrupted と区別し retry 計上なし
                    }
                    else if (result.IsOk)
                    {
                        job.State = JobState.Done;
                    }
                    else if (job.RetryCount < _maxRetries)
                    {
                        job.RetryCount++;                 // 1件の失敗で全体が止まらない：再キューして continue
                        job.State = JobState.Queued;
                        job.Error = result.Error;
                    }
                    else
                    {
                        job.State = JobState.Failed;
                        job.Error = result.Error;
                    }
                    PersistLocked();
                }
                Raise(job); // ロック外で発火
                return result;
            }
        }
        finally
        {
            lock (_lock) _processing = false;
        }
    }

    /// <summary>キューが空になるか ct がキャンセルされるまで ProcessNext を回す単一ワーカー。</summary>
    public async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var r = await ProcessNextAsync(ct).ConfigureAwait(false);
            if (r == null) break;
        }
    }

    /// <summary>ジョブをキャンセル/除去。Queued は Cancelled にして除去（二度と処理されない）、
    /// Running は CTS を発火し handler の ct 観察に委ねる。</summary>
    public bool Cancel(string jobId)
    {
        JobRecord? removed = null;
        lock (_lock)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (job == null) return false;
            if (job.State == JobState.Queued)
            {
                job.State = JobState.Cancelled;
                job.FinishedAt = _clock.GetUtcNow();
                _jobs.Remove(job);
                PersistLocked();
                removed = job;
            }
            else if (job.State == JobState.Running && _currentJob == job)
            {
                _currentCts?.Cancel();
            }
        }
        if (removed != null) Raise(removed); // ロック外で発火
        return true;
    }

    /// <summary>Queued の分をまとめて除去して件数を返す（「キュークリア」用）。</summary>
    public int ClearQueue()
    {
        List<JobRecord> removed;
        lock (_lock)
        {
            removed = _jobs.Where(j => j.State == JobState.Queued).ToList();
            // Cancelと同一意味論：Cancelled化してから除去。これがないと
            // ProcessNextAsyncの再ロック後再チェック（Queued）をすり抜けて実行される。
            foreach (var j in removed) j.State = JobState.Cancelled;
            _jobs.RemoveAll(removed.Contains);
            PersistLocked();
        }
        foreach (var j in removed) Raise(j); // ロック外で発火
        return removed.Count;
    }

    private void PersistLocked()
    {
        // I/O失敗は飲む：Enqueue（UIスレッド直下）やワーカーへ例外を伝播させない
        // （伝播するとジョブがRunningのままゾンビ化しdedupで永久に再enqueue不可）。
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // 原子的置換：temp へ書いてから Move（半書き込みで上書きしない）
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_jobs));
            try
            {
                File.Move(tmp, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CoreLog.Warn("jobs.json の保存に失敗", ex);
                try { File.Delete(tmp); } catch { /* クリーンアップ失敗は許容 */ }
            }
        }
        catch (Exception ex)
        {
            CoreLog.Warn("jobs.json の保存に失敗", ex);
        }
    }

    private void Raise(JobRecord job)
    {
        if (StateChanged == null) return;
        // UIハンドラの例外がキュー本体（Enqueue/ProcessNext）を壊さないよう捕捉してログ
        try { StateChanged(job); }
        catch (Exception ex) { CoreLog.Warn("StateChanged ハンドラが例外を投げました", ex); }
    }
}
