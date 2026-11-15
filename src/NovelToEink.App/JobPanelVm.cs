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
            JobState.Queued => Locale.T("label_waiting"),
            JobState.Running => Locale.T("label_running"),
            JobState.Done => Locale.T("label_done"),
            JobState.Failed => Locale.T("label_failed"),
            JobState.Cancelled => Locale.T("CancelBtn"),
            JobState.Interrupted => Locale.T("label_interrupted"),
            _ => job.State.ToString(),
        };
        Error = job.Error ?? "";
    }

    public string ActionLabel { get; }
    public string Title { get; }
    public string StateLabel { get; }
    public string Error { get; }
    public string Display => Error == ""
        ? Locale.T("jobline_fmt", ActionLabel, Title, StateLabel)
        : Locale.T("jobline_err_fmt", ActionLabel, Title, StateLabel, Error);

    public static string Label(JobAction action) => action switch
    {
        JobAction.Check => Locale.T("label_update_check"),
        JobAction.Download => Locale.T("label_download"),
        JobAction.Update => Locale.T("label_update"),
        JobAction.Convert => Locale.T("label_xtc"),
        _ => action.ToString(),
    };
}
