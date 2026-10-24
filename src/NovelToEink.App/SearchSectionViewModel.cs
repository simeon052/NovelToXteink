using System.Collections.ObjectModel;
using NovelToEink.Core;

namespace NovelToEink.App;

/// <summary>キーワード検索セクション：検索 → タイトル・作者・話数・完結状況を一覧表示 → チェックした作品を一括ダウンロード。</summary>
public sealed class SearchSectionViewModel : ViewModelBase
{
    private const int MaxResults = 20;

    private readonly LibrarySectionViewModel _library;
    private string _keyword = "";

    public SearchSectionViewModel(LibrarySectionViewModel library)
    {
        _library = library;
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => CanSearch);
        DownloadSelectedCommand = new AsyncRelayCommand(DownloadSelectedAsync, () => !_library.IsBusy && SelectedCount > 0);
        SelectAllCommand = new RelayCommand(SelectAll, () => !_library.IsBusy && Results.Count > 0);
    }

    /// <summary>検索結果のリスト（IsSelected で複数選択）。</summary>
    public ObservableCollection<SearchResultVm> Results { get; } = [];

    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand DownloadSelectedCommand { get; }
    public RelayCommand SelectAllCommand { get; }

    /// <summary>MainViewModelのStatusText更新用コールバック。</summary>
    public Action<string>? StatusSetter { get; set; }

    public string Keyword
    {
        get => _keyword;
        set { if (Set(ref _keyword, value)) OnChanged(nameof(CanSearch)); }
    }

    public bool CanSearch => !string.IsNullOrWhiteSpace(Keyword) && !_library.IsBusy;

    public int SelectedCount => Results.Count(r => r.IsSelected);

    private void NotifySelectionChanged() => OnChanged(nameof(SelectedCount));

    private void SelectAll()
    {
        foreach (var r in Results) r.IsSelected = true;
        OnChanged(nameof(SelectedCount));
    }

    private async Task SearchAsync()
    {
        var query = Keyword.Trim();
        var cts = _library.NewCts();
        _library.SetIsBusy(true);
        _library.StatusSetter?.Invoke($"キーワードを検索中: {query}");

        try
        {
            var hits = await NovelSearchService.SearchAsync(query, MaxResults, cts.Token);
            Results.Clear();
            foreach (var hit in hits) Results.Add(new SearchResultVm(hit, NotifySelectionChanged));
            OnChanged(nameof(SelectedCount));

            _library.StatusSetter?.Invoke(hits.Count == 0
                ? $"「{query}」に一致する作品が見つかりませんでした。"
                : $"{hits.Count} 件ヒットしました。チェックして「選択した作品を一括ダウンロード」へ。");
        }
        catch (OperationCanceledException) { _library.StatusSetter?.Invoke("検索を中断しました。"); }
        catch (Exception ex)
        {
            CoreLog.Warn("検索に失敗", ex);
            _library.StatusSetter?.Invoke("検索に失敗しました: " + ex.Message);
        }
        finally { _library.SetIsBusy(false); }
    }

    private async Task DownloadSelectedAsync()
    {
        var urls = Results.Where(r => r.IsSelected).Select(r => r.Url).ToList();
        if (urls.Count == 0) return;
        await _library.AddUrlsFromSearchAsync(urls);
    }
}
