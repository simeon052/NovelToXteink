using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using NovelToEink.Core;

namespace NovelToEink.App;

/// <summary>Core の JobQueue と UI を繋ぐ調停役：バックグラウンドワーカーループ、
/// パネル表示、enqueue ヘルパー。キュー経路はモーダル（表紙ピッカー）を持たない
/// （AutoCover オフなら coverPending で登録される）。OnUi（UIスレッドマーシャリング）
/// によりStateChangedは常にUIスレッドで処理される。</summary>
public sealed class JobQueueCoordinator
{
    public JobQueue Queue { get; }
    public ObservableCollection<JobPanelVm> PanelItems { get; } = new();

    private readonly AppSettings _settings;
    private readonly LibraryService _service;
    private readonly CancellationTokenSource _workerCts = new();
    private Action<string>? _status;
    private Action<JobRecord?> _displayRefresh = _ => { };

    public JobQueueCoordinator(AppSettings settings, LibraryService service)
    {
        _settings = settings;
        _service = service;
        Queue = new JobQueue(
            Path.Combine(service.Store.Folder, "jobs.json"),
            new AppJobHandler(this),
            TimeProvider.System);
        Queue.StateChanged += job => OnUi(() => OnJobChanged(job));
    }

    public AppSettings Settings => _settings;
    public LibraryService Service => _service;

    /// <summary>MainViewModel から UI 回调を注入される（status / display refresh）。</summary>
    public void SetUi(Action<string> status, Action<JobRecord?> displayRefresh)
    {
        _status = status;
        _displayRefresh = displayRefresh;
        Queue.Load();               // jobs.json 読み込み（クラッシュ由来の Running → Interrupted）
        RefreshPanel(null);
    }

    public JobRecord? Enqueue(LibraryEntry entry, JobAction action)
        => Queue.Enqueue(entry.WorkId, action, entry.Url, entry.Site);

    public JobRecord? EnqueueUrl(string url, JobAction action)
        => Queue.Enqueue("", action, url, JobQueue.SiteFromUrl(url));

    /// <summary>ジョブ一覧（ViewModelからの重複防止/削除ガード用）。</summary>
    public IEnumerable<JobRecord> Jobs => Queue.Jobs;

    public bool IsRunning => Queue.CurrentJob != null;
    public int QueuedCount => Queue.Jobs.Count(j => j.State == JobState.Queued);

    public LibraryEntry? FindEntry(JobRecord job)
        => _service.SnapshotEntries().FirstOrDefault(e => e.Url == job.Url
            || (job.WorkId != "" && e.WorkId == job.WorkId && e.Site == job.Site));

    /// <summary>ワーカー起動：キューが空のときは待機し、追加されれば拾う（UIをブロックしない）。</summary>
    public void StartWorker()
    {
        _ = Task.Run(async () =>
        {
            while (!_workerCts.Token.IsCancellationRequested)
            {
                JobResult? r = null;
                try { r = await Queue.ProcessNextAsync(_workerCts.Token); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { CoreLog.Error($"キュージョブの想定外失敗: {Queue.CurrentJob?.Id}", ex); }

                if (r == null)
                {
                    try { await Task.Delay(250, _workerCts.Token); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }, _workerCts.Token);
    }

    /// <summary>進行中ジョブをキャンセルする（テスト済みの Cancel セマンティクス）。</summary>
    public void CancelCurrent()
    {
        if (Queue.CurrentJob != null) Queue.Cancel(Queue.CurrentJob.Id);
    }

    public void ReportProgress(string message) => OnUi(() => _status?.Invoke("[Queue] " + message));

    private void OnJobChanged(JobRecord job)
    {
        RefreshPanel(job);
        _displayRefresh(job);

        var name = FindEntry(job)?.Title ?? job.Url;
        switch (job.State)
        {
            case JobState.Running: _status?.Invoke($"[Queue] 実行中: {name}"); break;
            case JobState.Done: _status?.Invoke($"[Queue] 完了: {name}"); break;
            case JobState.Failed: _status?.Invoke($"[Queue] 失敗: {name} — {job.Error}"); break;
            case JobState.Cancelled: _status?.Invoke($"[Queue] キャンセル: {name}"); break;
        }
    }

    private void RefreshPanel(JobRecord? lastChanged)
    {
        var pending = Queue.Jobs.Where(j => j.State == JobState.Queued || j.State == JobState.Running);
        var finished = Queue.Jobs.Where(j => j.State != JobState.Queued && j.State != JobState.Running)
            .OrderByDescending(j => j.FinishedAt ?? j.EnqueuedAt).Take(20);
        PanelItems.Clear();
        foreach (var job in pending.Concat(finished))
            PanelItems.Add(new JobPanelVm(job, FindEntry(job)?.Title ?? job.Url));
    }

    private static void OnUi(Action action)
    {
        var app = Application.Current;
        if (app == null) { action(); return; }
        if (app.Dispatcher.CheckAccess()) action();
        else app.Dispatcher.Invoke(action);
    }
}
