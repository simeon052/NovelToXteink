using System.IO;
using NovelToEink.Core;
using NovelToEink.Xtc;

namespace NovelToEink.App;

/// <summary>App層のジョブハンドラ。Check/Download/Update は Core の JobHandler（テスト済み、
/// キャッシュ優先）へ委譲し、Convert は Core 側の再構築+登録後に EPUB→XTC 変換を実行する。</summary>
public sealed class AppJobHandler : IJobHandler
{
    private readonly JobQueueCoordinator _c;
    private readonly JobHandler _core;

    public AppJobHandler(JobQueueCoordinator coordinator)
    {
        _c = coordinator;
        // キュー経路にモーダルはない → 表紙 resolver は null（AutoCover オフなら coverPending）。
        _core = new JobHandler(coordinator.Service, new NovelDownloadService(), null,
            new Progress<string>(m => coordinator.ReportProgress(m)));
    }

    public async Task<JobResult> ExecuteAsync(JobRecord job, CancellationToken ct)
    {
        var r = await _core.ExecuteAsync(job, ct);   // Convert = cache再構築+登録（テスト済み）
        if (job.Action != JobAction.Convert) return r;
        if (!r.IsOk) return r;

        var entry = _c.FindEntry(job);
        if (entry == null) return JobResult.Fail("エントリが見つかりません");

        var options = XtcConversionUtility.BuildOptions(_c.Settings.ToEpubOptions());
        var parts = entry.EpubParts.Count > 0 ? entry.EpubParts : new List<string> { entry.EpubPath };
        foreach (var epub in parts)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(epub)) return JobResult.Fail($"EPUBファイルが見つかりません: {epub}");
            var progress = new Progress<XtcProgress>(p =>
                _c.ReportProgress($"XTC変換: {p.ChapterIndex}/{p.ChapterCount}話 / {p.PagesEmitted}ページ"));
            await XtcConversionUtility.ConvertEpubToXtcAsync(
                epub, Path.ChangeExtension(epub, ".xtc"), options, progress, ct).ConfigureAwait(false);
        }
        return JobResult.Ok();
    }
}
