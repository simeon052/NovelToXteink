using NovelToEink.Core;

namespace NovelToEink.App;

/// <summary>キューパネルの1件表示。StateChanged のたびに再構築されるため INPC 不要。</summary>
public sealed class JobPanelVm
{
    public JobPanelVm(JobRecord job, string title)
    {
        ActionLabel = Label(job.Action);
        Title = title;
        StateLabel = job.State switch
        {
            JobState.Queued => "待ち",
            JobState.Running => "実行中",
            JobState.Done => "完了",
            JobState.Failed => "失敗",
            JobState.Cancelled => "キャンセル",
            JobState.Interrupted => "中断(再起動時)",
            _ => job.State.ToString(),
        };
        Error = job.Error ?? "";
    }

    public string ActionLabel { get; }
    public string Title { get; }
    public string StateLabel { get; }
    public string Error { get; }
    public string Display => Error == ""
        ? $"{ActionLabel}「{Title}」 — {StateLabel}"
        : $"{ActionLabel}「{Title}」 — {StateLabel}: {Error}";

    public static string Label(JobAction action) => action switch
    {
        JobAction.Check => "更新チェック",
        JobAction.Download => "ダウンロード",
        JobAction.Update => "更新",
        JobAction.Convert => "XTC変換",
        _ => action.ToString(),
    };
}
