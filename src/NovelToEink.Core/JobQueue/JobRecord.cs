namespace NovelToEink.Core;

public enum JobState { Queued, Running, Done, Failed, Cancelled, Interrupted }

public enum JobAction { Download, Update, Check, Convert }

/// <summary>ジョブキューの1件。jobs.json に永続化される（UI非依存）。</summary>
public class JobRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WorkId { get; set; } = "";
    public JobAction Action { get; set; }

    /// <summary>目次URL（ViewModel連携用。単独Enqueueなら空可）。</summary>
    public string Url { get; set; } = "";

    /// <summary>サイト（キャッシュ-firstパスの解決に使用）。</summary>
    public NovelSite Site { get; set; }

    public JobState State { get; set; } = JobState.Queued;
    public int RetryCount { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset EnqueuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

public sealed class JobResult
{
    public bool IsOk { get; }
    public string? Error { get; }
    private JobResult(bool ok, string? error) { IsOk = ok; Error = error; }
    public static JobResult Ok() => new(true, null);
    public static JobResult Fail(string error) => new(false, error);
}

/// <summary>ジョブ実行の抽象。JobQueue（Core）がテスト可能であるための seam。
/// 実装は必ず ct を観察すること（観察しなければ q.Cancel() の Running 路径は
/// CTS を発火するだけで、状態は handler の戻り値で決定される）。</summary>
public interface IJobHandler
{
    Task<JobResult> ExecuteAsync(JobRecord job, CancellationToken ct);
}
