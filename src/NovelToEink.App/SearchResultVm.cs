using System.Windows.Media;
using NovelToEink.Core;

namespace NovelToEink.App;

/// <summary>キーワード検索の 1 件（リスト表示用。チェックで複数選択）。</summary>
public sealed class SearchResultVm : ViewModelBase
{
    private static readonly Brush CompletedBrush = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
    private static readonly Brush OngoingBrush = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));

    static SearchResultVm()
    {
        // WPF の静的 Brush はフリーズ推奨（スレッド安全・パフォーマンス）
        CompletedBrush.Freeze();
        OngoingBrush.Freeze();
    }

    private readonly Action _selectionChanged;
    private bool _isSelected;

    public SearchResultVm(NovelSearchResult result, Action selectionChanged)
    {
        Result = result;
        _selectionChanged = selectionChanged;
    }

    public NovelSearchResult Result { get; }

    public string Url => Result.Url;

    public bool IsSelected
    {
        get => _isSelected;
        set { if (Set(ref _isSelected, value)) _selectionChanged(); }
    }

    public string Title => Result.Title;
    public string Author => string.IsNullOrWhiteSpace(Result.Author) ? Locale.T("author_unknown") : Result.Author;
    public string SiteLabel => Result.Site switch
    {
        NovelSite.Syosetu => Locale.T("src_syosetu"),
        NovelSite.Kakuyomu => Locale.T("src_kakuyomu"),
        _ => Result.Site.ToString(),
    };
    public string EpisodesText => Result.EpisodeCount > 0 ? Locale.T("episodes_short_fmt", Result.EpisodeCount) : Locale.T("unknown_short");
    public string StatusText => Result.IsCompleted switch { true => Locale.T("col_completed"), false => Locale.T("serializing"), _ => Locale.T("unknown_short") };
    public Brush StatusBrush => Result.IsCompleted == true ? CompletedBrush : OngoingBrush;
}
