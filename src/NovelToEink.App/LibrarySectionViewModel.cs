using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using NovelToEink.Core;
using NovelToEink.Xtc;

namespace NovelToEink.App;

/// <summary>
/// ライブラリ操作（追加・更新・削除・ソート・表紙選択・XTC変換）のサブビューモデル。
/// </summary>
public sealed class LibrarySectionViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly LibraryService _service;
    private CancellationTokenSource? _cts;

    private enum SortKey { Converted, SiteUpdated, Title }
    private SortKey _sortKey = SortKey.Converted;

    /// <summary>ライブラリ一覧。</summary>
    public ObservableCollection<LibraryItemVm> Items { get; } = [];

    /// <summary>表紙未選択の作品数。</summary>
    public int PendingCoverCount => Items.Count(i => i.Entry.CoverPending);

    public LibrarySectionViewModel(AppSettings settings, LibraryService service)
    {
        _settings = settings;
        _service = service;
        Items.CollectionChanged += (_, _) => OnChanged(nameof(PickPendingCoversLabel));
        ReloadItems();
    }

    // ---- 状態（MainViewModelと共有） ----

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set { if (Set(ref _isBusy, value)) OnChanged(nameof(IsNotBusy)); } }
    public bool IsNotBusy => !IsBusy;

    private double _progressValue;
    public double ProgressValue { get => _progressValue; set => Set(ref _progressValue, value); }

    private bool _progressIndeterminate;
    public bool ProgressIndeterminate { get => _progressIndeterminate; set => Set(ref _progressIndeterminate, value); }

    // ---- コマンド ----

    public RelayCommand ChooseFolderCommand { get; private set; }
    public RelayCommand CancelCommand { get; private set; }
    public RelayCommand<LibraryItemVm> RemoveCommand { get; private set; }
    public RelayCommand<LibraryItemVm> RenameCommand { get; private set; }
    public RelayCommand<LibraryItemVm> CopyTitleCommand { get; private set; }
    public RelayCommand<LibraryItemVm> OpenUrlCommand { get; private set; }

    // ライブラリ操作系（非同期）
    public AsyncRelayCommand AddCommand { get; private set; }
    public AsyncRelayCommand CheckAllCommand { get; private set; }
    public AsyncRelayCommand UpdateAllCommand { get; private set; }
    public AsyncRelayCommand<LibraryItemVm> CheckItemCommand { get; private set; }
    public AsyncRelayCommand<LibraryItemVm> UpdateItemCommand { get; private set; }
    public AsyncRelayCommand<LibraryItemVm> ChangeCoverCommand { get; private set; }
    public AsyncRelayCommand PickPendingCoversCommand { get; private set; }
    public RelayCommand<LibraryItemVm> OpenEpubCommand { get; private set; }
    public RelayCommand<LibraryItemVm> OpenFolderCommand { get; private set; }

    // XTC変換系
    public AsyncRelayCommand<LibraryItemVm> ConvertXtcCommand { get; private set; }
    public AsyncRelayCommand ConvertAllXtcCommand { get; private set; }

    /// <summary>「表紙をまとめて選ぶ」ラベル。</summary>
    public string PickPendingCoversLabel => PendingCoverCount > 0
        ? $"🖼 表紙をまとめて選ぶ ({PendingCoverCount})"
        : "🖼 表紙をまとめて選ぶ";

    // ---- ソート ----

    public bool IsSortConverted
    {
        get => _sortKey == SortKey.Converted;
        set { if (value && _sortKey != SortKey.Converted) { _sortKey = SortKey.Converted; ApplySort(); } }
    }
    public bool IsSortSiteUpdated
    {
        get => _sortKey == SortKey.SiteUpdated;
        set { if (value && _sortKey != SortKey.SiteUpdated) { _sortKey = SortKey.SiteUpdated; ApplySort(); } }
    }
    public bool IsSortTitle
    {
        get => _sortKey == SortKey.Title;
        set { if (value && _sortKey != SortKey.Title) { _sortKey = SortKey.Title; ApplySort(); } }
    }

    // ---- 入力 ----

    private string _urlsText = "";
    public string UrlsText
    {
        get => _urlsText;
        set { if (Set(ref _urlsText, value)) OnChanged(nameof(HasInputs)); }
    }

    /// <summary>URLまたはタイトルが1件以上入力されているか。</summary>
    public bool HasInputs => ParseInputs(_urlsText).Count > 0;

    // ---- ステータス・進捗通知（MainViewModelに委譲） ----

    /// <summary>MainViewModelのStatusText更新用コールバック。</summary>
    public Action<string>? StatusSetter { get; set; }

    private void SetStatus(string msg) => StatusSetter?.Invoke(msg);

    // ---- 初期化 ----

    public void InitCommands(
        AsyncRelayCommand add,
        AsyncRelayCommand checkAll,
        AsyncRelayCommand updateAll,
        AsyncRelayCommand<LibraryItemVm> checkItem,
        AsyncRelayCommand<LibraryItemVm> updateItem,
        AsyncRelayCommand<LibraryItemVm> changeCover,
        AsyncRelayCommand pickPendingCovers,
        RelayCommand<LibraryItemVm> openEpub,
        RelayCommand<LibraryItemVm> openFolder,
        AsyncRelayCommand<LibraryItemVm> convertXtc,
        AsyncRelayCommand convertAllXtc)
    {
        AddCommand = add;
        CheckAllCommand = checkAll;
        UpdateAllCommand = updateAll;
        CheckItemCommand = checkItem;
        UpdateItemCommand = updateItem;
        ChangeCoverCommand = changeCover;
        PickPendingCoversCommand = pickPendingCovers;
        OpenEpubCommand = openEpub;
        OpenFolderCommand = openFolder;
        ConvertXtcCommand = convertXtc;
        ConvertAllXtcCommand = convertAllXtc;

        ChooseFolderCommand = new RelayCommand(ChooseFolder, () => !IsBusy);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
        RemoveCommand = new RelayCommand<LibraryItemVm>(Remove, _ => !IsBusy);
        RenameCommand = new RelayCommand<LibraryItemVm>(Rename, _ => !IsBusy);
        CopyTitleCommand = new RelayCommand<LibraryItemVm>(CopyTitle, _ => !IsBusy);
        OpenUrlCommand = new RelayCommand<LibraryItemVm>(OpenUrl, _ => !IsBusy);
    }

    // ---- 公共メソッド（MainViewModelから呼ばれる） ----

    public void SetUrlsText(string text) => UrlsText = text;
    public string GetUrlsText() => UrlsText;

    /// <summary>メインビューモデルの IsBusy を更新。</summary>
    public void SetIsBusy(bool value) => IsBusy = value;

    /// <summary>メインビューモデルの進捗を更新。</summary>
    public void SetProgress(double value, bool indeterminate = false)
    {
        ProgressValue = value;
        ProgressIndeterminate = indeterminate;
    }

    // ---- ライブラリ操作 ----

    public async Task AddAsync()
    {
        var inputs = ParseInputs(_urlsText);
        if (inputs.Count == 0) { SetStatus("URLまたは作品タイトルを入力してください。"); return; }

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var dlProgress = MakeDlProgress();
        var buildProgress = new Progress<string>(m => SetStatus("[EPUB] " + m));
        int added = 0, skipped = 0, failed = 0;
        string? lastError = null;

        try
        {
            foreach (var input in inputs)
            {
                _cts.Token.ThrowIfCancellationRequested();
                try
                {
                    var url = await ResolveInputAsync(input, _cts.Token);
                    if (url == null) { skipped++; continue; }

                    SetStatus($"ダウンロード中: {url}");
                    var novel = await _service.DownloadAsync(url, Options, dlProgress, _cts.Token);

                    var cover = await PickCoverAsync(novel);
                    if (cover.Cancelled) { skipped++; continue; }

                    SetStatus("EPUBを生成中…");
                    var entry = await Task.Run(() =>
                        _service.BuildAndRegister(novel, cover.Image, Options, buildProgress, coverPending: cover.IsProvisional));
                    UpsertItem(entry);
                    added++;

                    if (cover.IsProvisional) PrefetchCoverCandidates(entry);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { failed++; lastError = ex.Message; }
            }

            UrlsText = "";
            SetStatus($"追加 {added} 件" + (skipped > 0 ? $" / スキップ {skipped}" : "")
                         + (failed > 0 ? $" / 失敗 {failed}（{lastError}）" : "") + "。"
                         + (PendingCoverCount > 0 ? "表紙は「表紙をまとめて選ぶ」で選べます。" : ""));
        }
        catch (OperationCanceledException) { SetStatus($"中断しました（追加 {added} 件）。"); }
        finally { EndBusy(); OnChanged(nameof(PickPendingCoversLabel)); }
    }

    private async Task<string?> ResolveInputAsync(string input, CancellationToken token)
    {
        if (NovelSearchService.LooksLikeUrl(input))
        {
            var url = input.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? input : "https://" + input;
            if (_service.IsSupported(url)) return url;
            SetStatus($"対応していないURLです（なろう / カクヨムのみ）: {input}");
            return null;
        }

        SetStatus($"タイトルを検索中: {input}");
        var hits = await NovelSearchService.SearchAsync(input, 5, token);
        if (hits.Count == 0)
        {
            SetStatus($"「{input}」に一致する作品が見つかりませんでした。");
            return null;
        }

        var best = hits[0];
        var others = hits.Count > 1 ? $"（他 {hits.Count - 1} 件の候補あり）" : "";
        SetStatus($"「{input}」→ {best.Label} {others}");
        return best.Url;
    }

    private async Task<(ScrapedImage? Image, bool IsProvisional, bool Cancelled)> PickCoverAsync(NovelDownload novel)
    {
        if (_settings.AutoCover)
            return (await Task.Run(() => CoverSelection.PickProvisional(novel, Options)), true, false);

        var dlg = new CoverPickerWindow(novel, Options) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return (null, false, true);
        return (dlg.Result?.Image, false, false);
    }

    private void PrefetchCoverCandidates(LibraryEntry entry)
    {
        var outputFolder = _settings.OutputFolder;
        var title = entry.Title;
        var author = entry.Author;
        var site = entry.Site;
        var workId = entry.WorkId;

        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var images = await ImageSearchService.SearchAsync(title, author, 5, cts.Token);
                if (images.Count == 0) return;

                CoverCandidateCache.Save(outputFolder, site, workId, images);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var item = Items.FirstOrDefault(i => i.Entry.Site == site && i.Entry.WorkId == workId);
                    item?.SetCoverCandidateCount(images.Count);
                });
            }
            catch (Exception ex) { CoreLog.Ignored($"表紙候補を集められない（本処理には影響しない）: {workId}", ex); }
        });
    }

    public async Task CheckAllAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        var updates = 0;
        try
        {
            foreach (var item in Items)
            {
                _cts.Token.ThrowIfCancellationRequested();
                item.IsBusy = true; item.BusyText = "確認中…";
                try
                {
                    var has = await _service.CheckAsync(item.Entry, _cts.Token);
                    if (has) updates++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    item.Entry.Status = UpdateStatus.Error;
                    CoreLog.Warn($"更新チェックに失敗: {item.Entry.Title}", ex);
                }
                finally { item.IsBusy = false; item.Refresh(); }
            }
            SetStatus(updates > 0
                ? $"更新チェック完了：{updates} 作品に更新があります。"
                : "更新チェック完了：すべて最新です。");
        }
        catch (OperationCanceledException) { SetStatus("更新チェックを中断しました。"); }
        finally { EndBusy(); }
    }

    public async Task UpdateAllAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        var targets = Items.Where(i => i.HasUpdate).ToList();
        var done = 0;
        try
        {
            foreach (var item in targets)
            {
                _cts.Token.ThrowIfCancellationRequested();
                await UpdateOne(item);
                done++;
            }
            SetStatus($"{done} 作品を更新しました。");
        }
        catch (OperationCanceledException) { SetStatus($"更新を中断しました（{done} 件完了）。"); }
        finally { EndBusy(); }
    }

    public async Task CheckItemAsync(LibraryItemVm? item)
    {
        if (item == null) return;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        item.IsBusy = true; item.BusyText = "確認中…";
        try
        {
            var has = await _service.CheckAsync(item.Entry, _cts.Token);
            SetStatus(has ? $"「{item.Title}」に更新があります。" : $"「{item.Title}」は最新です。");
        }
        catch (OperationCanceledException) { SetStatus("中断しました。"); }
        catch (Exception ex) { item.Entry.Status = UpdateStatus.Error; SetStatus("確認失敗：" + ex.Message); }
        finally { item.IsBusy = false; item.Refresh(); EndBusy(); }
    }

    public async Task UpdateItemAsync(LibraryItemVm? item)
    {
        if (item == null) return;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        try { await UpdateOne(item); SetStatus($"「{item.Title}」を更新しました。"); }
        catch (OperationCanceledException) { SetStatus("更新を中断しました。"); }
        catch (Exception ex) { SetStatus("更新失敗：" + ex.Message); }
        finally { EndBusy(); }
    }

    private async Task UpdateOne(LibraryItemVm item)
    {
        item.IsBusy = true; item.BusyText = "更新中…";
        var dlProgress = MakeDlProgress();
        var buildProgress = new Progress<string>(m => SetStatus("[EPUB] " + m));
        try
        {
            await _service.UpdateAsync(item.Entry, dlProgress, buildProgress, _cts!.Token);
        }
        finally { item.IsBusy = false; item.Refresh(); }
    }

    public async Task ChangeCoverAsync(LibraryItemVm? item)
    {
        if (item == null) return;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        try
        {
            SetStatus("表紙変更のため再取得中…");
            var picked = await ChooseCoverAsync(item, _cts.Token);
            SetStatus(picked ? $"「{item.Title}」の表紙を変更しました。" : "表紙変更をキャンセルしました。");
        }
        catch (OperationCanceledException) { SetStatus("中断しました。"); }
        catch (Exception ex) { SetStatus("表紙変更失敗：" + ex.Message); }
        finally { EndBusy(); OnChanged(nameof(PickPendingCoversLabel)); }
    }

    public async Task PickPendingCoversAsync()
    {
        var pending = Items.Where(i => i.Entry.CoverPending).ToList();
        if (pending.Count == 0) return;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        int picked = 0, skipped = 0, failed = 0;
        string? lastError = null;
        try
        {
            for (var i = 0; i < pending.Count; i++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                SetStatus($"({i + 1}/{pending.Count}) 「{pending[i].Title}」の表紙を選択中…");
                try
                {
                    if (await ChooseCoverAsync(pending[i], _cts.Token)) picked++;
                    else skipped++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { failed++; lastError = ex.Message; }
            }
            SetStatus($"表紙を選択 {picked} 件" + (skipped > 0 ? $" / スキップ {skipped}" : "")
                         + (failed > 0 ? $" / 失敗 {failed}（{lastError}）" : "") + "。");
        }
        catch (OperationCanceledException) { SetStatus($"中断しました（表紙を選択 {picked} 件）。"); }
        finally { EndBusy(); OnChanged(nameof(PickPendingCoversLabel)); }
    }

    private async Task<bool> ChooseCoverAsync(LibraryItemVm item, CancellationToken ct)
    {
        var dlProgress = MakeDlProgress();
        var buildProgress = new Progress<string>(m => SetStatus("[EPUB] " + m));
        item.IsBusy = true; item.BusyText = "再取得中…";
        try
        {
            var novel = await _service.DownloadAsync(item.Entry.Url, item.Entry.Options, dlProgress, ct);
            item.IsBusy = false;

            var prefetched = await Task.Run(() =>
                CoverCandidateCache.Load(_settings.OutputFolder, item.Entry.Site, item.Entry.WorkId));

            var dlg = new CoverPickerWindow(novel, item.Entry.Options, prefetched)
            {
                Owner = Application.Current.MainWindow,
            };
            if (dlg.ShowDialog() != true) return false;
            var cover = dlg.Result?.Image;
            item.IsBusy = true; item.BusyText = "再生成中…";
            await Task.Run(() => _service.BuildAndRegister(novel, cover, item.Entry.Options, buildProgress));
            item.SetCoverCandidateCount(0);
            return true;
        }
        finally { item.IsBusy = false; item.Refresh(); }
    }

    // ---- XTC 変換 ----

    public async Task ConvertXtcAsync(LibraryItemVm? item)
    {
        if (item == null) return;

        var parts = GetEpubParts(item);
        if (parts.Count == 0) { SetStatus("EPUBファイルが見つかりません。先に更新してください。"); return; }

        IsBusy = true;
        _cts = new CancellationTokenSource();
        try
        {
            var pages = await ConvertPartsAsync(item, parts, _cts.Token);
            SetStatus($"XTC変換完了: {item.Title}（{parts.Count} ファイル / 計 {pages} ページ）");
        }
        catch (OperationCanceledException) { SetStatus("XTC変換をキャンセルしました。"); }
        catch (Exception ex) { SetStatus("XTC変換に失敗しました：" + ex.Message); }
        finally
        {
            item.IsBusy = false;
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
            ProgressIndeterminate = false;
            ProgressValue = 0;
        }
    }

    public async Task ConvertAllXtcAsync()
    {
        var targets = Items.Where(i => GetEpubParts(i).Count > 0).ToList();
        if (targets.Count == 0) { SetStatus("変換できるEPUBがありません。"); return; }

        IsBusy = true;
        _cts = new CancellationTokenSource();
        int done = 0, failed = 0;
        try
        {
            foreach (var item in targets)
            {
                _cts.Token.ThrowIfCancellationRequested();
                try
                {
                    await ConvertPartsAsync(item, GetEpubParts(item), _cts.Token);
                    done++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    failed++;
                    SetStatus($"{item.Title}: {ex.Message}");
                }
                finally { item.IsBusy = false; }
            }
            SetStatus($"XTC一括変換 完了: 成功 {done} / 失敗 {failed}");
        }
        catch (OperationCanceledException) { SetStatus($"XTC一括変換を中断しました（成功 {done}）。"); }
        finally
        {
            foreach (var item in targets) item.IsBusy = false;
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
            ProgressIndeterminate = false;
            ProgressValue = 0;
        }
    }

    private async Task<int> ConvertPartsAsync(LibraryItemVm item, IReadOnlyList<string> parts, CancellationToken token)
    {
        var options = XtcConversionUtility.BuildOptions(EpubSettings.ToEpubOptions());
        var totalPages = 0;

        item.IsBusy = true;
        for (var index = 0; index < parts.Count; index++)
        {
            token.ThrowIfCancellationRequested();

            var epubPath = parts[index];
            var xtcPath = Path.ChangeExtension(epubPath, ".xtc");
            var label = parts.Count > 1 ? $"{item.Title} ({index + 1}/{parts.Count})" : item.Title;

            var progress = new Progress<XtcProgress>(p =>
            {
                ProgressIndeterminate = p.ChapterCount == 0;
                if (p.ChapterCount > 0) ProgressValue = 100.0 * p.ChapterIndex / p.ChapterCount;
                item.BusyText = $"XTC変換中… {p.PagesEmitted} ページ";
                SetStatus($"[XTC] {label}  {p.ChapterIndex}/{p.ChapterCount} 章  {p.PagesEmitted} ページ");
            });

            var result = await XtcConversionUtility.ConvertEpubToXtcAsync(epubPath, xtcPath, options, progress, token);
            totalPages += result.PageCount;
        }

        return totalPages;
    }

    private static List<string> GetEpubParts(LibraryItemVm item)
    {
        var parts = item.Entry.EpubParts.Count > 0
            ? item.Entry.EpubParts
            : [item.Entry.EpubPath];
        return parts.Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p)).ToList();
    }

    // ---- ファイル操作 ----

    public void OpenEpub(LibraryItemVm? item)
    {
        if (item == null) return;
        if (!File.Exists(item.Entry.EpubPath)) { SetStatus("EPUBファイルが見つかりません。"); return; }
        try { Process.Start(new ProcessStartInfo(item.Entry.EpubPath) { UseShellExecute = true }); }
        catch (Exception ex) { SetStatus("起動失敗：" + ex.Message); }
    }

    public void OpenFolder(LibraryItemVm? item)
    {
        if (item == null) return;
        var path = item.Entry.EpubPath;
        try
        {
            if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else Process.Start("explorer.exe", $"\"{_settings.OutputFolder}\"");
        }
        catch (Exception ex) { SetStatus("フォルダを開けません：" + ex.Message); }
    }

    private void Remove(LibraryItemVm? item)
    {
        if (item == null) return;
        var r = MessageBox.Show(
            $"「{item.Title}」をライブラリから削除します。\n\n「はい」=EPUBファイルも削除\n「いいえ」=一覧からのみ削除（ファイルは残す）",
            "削除の確認", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return;
        _service.Remove(item.Entry, deleteFiles: r == MessageBoxResult.Yes);
        Items.Remove(item);
        SetStatus($"「{item.Title}」を削除しました。");
    }

    private void Rename(LibraryItemVm? item)
    {
        if (item == null) return;
        try
        {
            _service.RenameFiles(item.Entry, item.NameTemplateEdit);
            item.Refresh();
            SetStatus($"「{item.Title}」のファイル名を変更しました。");
        }
        catch (Exception ex) { SetStatus("ファイル名変更失敗：" + ex.Message); }
    }

    private void CopyTitle(LibraryItemVm? item)
    {
        if (item == null) return;
        try
        {
            Clipboard.SetText(item.Title);
            SetStatus($"「{item.Title}」をクリップボードにコピーしました。");
        }
        catch (Exception ex) { SetStatus("コピー失敗：" + ex.Message); }
    }

    private void OpenUrl(LibraryItemVm? item)
    {
        if (item == null || string.IsNullOrEmpty(item.Url)) return;
        try { Process.Start(new ProcessStartInfo(item.Url) { UseShellExecute = true }); }
        catch (Exception ex) { SetStatus("ブラウザを開けません：" + ex.Message); }
    }

    // ---- フォルダ・ソート ----

    private void ChooseFolder()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "EPUBの保存先フォルダ",
            InitialDirectory = Directory.Exists(_settings.OutputFolder) ? _settings.OutputFolder : null,
        };
        if (dlg.ShowDialog() != true) return;
        _service.ChangeFolder(dlg.FolderName);
        ReloadItems();
        SetStatus($"保存先を変更しました：{dlg.FolderName}（{Items.Count} 作品を読み込み）");
    }

    public void ReloadItems()
    {
        Items.Clear();
        ApplySort();
    }

    private void ApplySort()
    {
        var sorted = _sortKey switch
        {
            SortKey.SiteUpdated => _service.Entries
                .OrderByDescending(e => e.SiteLastUpdated ?? DateTimeOffset.MinValue)
                .ThenByDescending(e => e.LastUpdatedAt ?? e.AddedAt),
            SortKey.Title => _service.Entries
                .OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase),
            _ => _service.Entries
                .OrderByDescending(e => e.LastUpdatedAt ?? e.AddedAt),
        };

        Items.Clear();
        foreach (var e in sorted)
        {
            var vm = new LibraryItemVm(e);
            vm.SetCoverCandidateCount(CoverCandidateCache.CountCandidates(_settings.OutputFolder, e.Site, e.WorkId));
            Items.Add(vm);
        }

        OnChanged(nameof(IsSortConverted));
        OnChanged(nameof(IsSortSiteUpdated));
        OnChanged(nameof(IsSortTitle));
    }

    private void UpsertItem(LibraryEntry entry)
    {
        var existing = Items.FirstOrDefault(i => i.Entry == entry);
        if (existing != null) existing.Refresh();
        else Items.Insert(0, new LibraryItemVm(entry));
    }

    // ---- ユーティリティ ----

    private void EndBusy()
    {
        IsBusy = false;
        ProgressIndeterminate = false;
        ProgressValue = 0;
    }

    private Progress<DownloadProgress> MakeDlProgress() => new(p =>
    {
        if (p.Total > 0) { ProgressIndeterminate = false; ProgressValue = 100.0 * p.Current / p.Total; }
        else ProgressIndeterminate = true;
        SetStatus($"[{p.Phase}] {p.Message}");
    });

    private EpubOptions Options => EpubSettings.ToEpubOptions();

    // ---- MainViewModelとの共有設定参照 ----

    /// <summary>MainViewModelのEpubSettingsへの参照。XTC変換時に必要。</summary>
    public EpubSettingsViewModel? EpubSettings { get; set; }

    private static List<string> ParseInputs(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var results = new List<string>();
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (NovelSearchService.LooksLikeUrl(line))
            {
                results.AddRange(line
                    .Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(NovelSearchService.LooksLikeUrl));
            }
            else
            {
                results.Add(line);
            }
        }

        return results.Distinct().ToList();
    }
}
