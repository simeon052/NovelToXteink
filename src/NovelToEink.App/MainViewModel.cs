using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using NovelToEink.Core;
using NovelToEink.Xtc;

namespace NovelToEink.App;

public sealed class MainViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly LibraryService _service;
    private CancellationTokenSource? _cts;

    private enum SortKey { Converted, SiteUpdated, Title }
    private SortKey _sortKey = SortKey.Converted;

    public ObservableCollection<LibraryItemVm> Items { get; } = [];

    public MainViewModel()
    {
        _settings = AppSettings.Load();
        _service = new LibraryService(_settings);
        ApplyTheme(_settings.IsDarkMode);
        ReloadItems();

        AddCommand = new AsyncRelayCommand(AddAsync, () => !IsBusy && HasInputs);
        CheckAllCommand = new AsyncRelayCommand(CheckAllAsync, () => !IsBusy && Items.Count > 0);
        UpdateAllCommand = new AsyncRelayCommand(UpdateAllAsync, () => !IsBusy && Items.Any(i => i.HasUpdate));
        ChooseFolderCommand = new RelayCommand(ChooseFolder, () => !IsBusy);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);

        CheckItemCommand = new AsyncRelayCommand<LibraryItemVm>(CheckItemAsync, _ => !IsBusy);
        UpdateItemCommand = new AsyncRelayCommand<LibraryItemVm>(UpdateItemAsync, _ => !IsBusy);
        ChangeCoverCommand = new AsyncRelayCommand<LibraryItemVm>(ChangeCoverAsync, _ => !IsBusy);
        OpenEpubCommand = new RelayCommand<LibraryItemVm>(OpenEpub);
        OpenFolderCommand = new RelayCommand<LibraryItemVm>(OpenFolder);
        RemoveCommand = new RelayCommand<LibraryItemVm>(Remove, _ => !IsBusy);
        RenameCommand = new RelayCommand<LibraryItemVm>(Rename, _ => !IsBusy);
        OpenProofreadingRulesCommand = new RelayCommand(OpenProofreadingRules);
        CopyTitleCommand = new RelayCommand<LibraryItemVm>(CopyTitle);
        OpenUrlCommand = new RelayCommand<LibraryItemVm>(OpenUrl);
        ToggleDarkModeCommand = new RelayCommand(ToggleDarkMode);
        ConvertXtcCommand = new AsyncRelayCommand<LibraryItemVm>(ConvertXtcAsync, _ => !IsBusy);
        ConvertAllXtcCommand = new AsyncRelayCommand(ConvertAllXtcAsync, () => !IsBusy && Items.Count > 0);
        ChooseXtcFontCommand = new RelayCommand(ChooseXtcFont, () => !IsBusy);
    }

    // ---- 入力・オプション ----
    private string _urlsText = "";
    public string UrlsText
    {
        get => _urlsText;
        set { if (Set(ref _urlsText, value)) OnChanged(nameof(HasInputs)); }
    }

    /// <summary>URLまたはタイトルが1件以上入力されているか。</summary>
    public bool HasInputs => ParseInputs(_urlsText).Count > 0;

    public string OutputFolder => _settings.OutputFolder;

    public bool Vertical
    {
        get => _settings.Vertical;
        set { if (_settings.Vertical != value) { _settings.Vertical = value; _settings.Save(); OnChanged(); } }
    }
    public bool GrayscaleImages
    {
        get => _settings.GrayscaleImages;
        set { if (_settings.GrayscaleImages != value) { _settings.GrayscaleImages = value; _settings.Save(); OnChanged(); } }
    }
    public bool IncludeInlineImages
    {
        get => _settings.IncludeInlineImages;
        set { if (_settings.IncludeInlineImages != value) { _settings.IncludeInlineImages = value; _settings.Save(); OnChanged(); } }
    }
    public bool KeepRuby
    {
        get => _settings.KeepRuby;
        set { if (_settings.KeepRuby != value) { _settings.KeepRuby = value; _settings.Save(); OnChanged(); } }
    }
    public int RequestDelayMs
    {
        get => _settings.RequestDelayMs;
        set { if (_settings.RequestDelayMs != value) { _settings.RequestDelayMs = value; _settings.Save(); OnChanged(); } }
    }
    public int EpisodesPerFile
    {
        get => _settings.EpisodesPerFile;
        set { if (_settings.EpisodesPerFile != value) { _settings.EpisodesPerFile = value; _settings.Save(); OnChanged(); } }
    }
    public bool EnableProofreading
    {
        get => _settings.EnableProofreading;
        set { if (_settings.EnableProofreading != value) { _settings.EnableProofreading = value; _settings.Save(); OnChanged(); } }
    }

    /// <summary>追加時に表紙選択で止まらず、暫定表紙で先へ進むか。</summary>
    public bool AutoCover
    {
        get => _settings.AutoCover;
        set { if (_settings.AutoCover != value) { _settings.AutoCover = value; _settings.Save(); OnChanged(); } }
    }

    // ---- XTC 出力 ----

    /// <summary>EPUB 生成後に XTC も作るか。</summary>
    public bool GenerateXtc
    {
        get => _settings.GenerateXtc;
        set { if (_settings.GenerateXtc != value) { _settings.GenerateXtc = value; _settings.Save(); OnChanged(); } }
    }

    /// <summary>選択できる端末の一覧。</summary>
    public IReadOnlyList<XteinkDevice> XtcDevices { get; } = [XteinkDevice.X3, XteinkDevice.X4Pro];

    /// <summary>XTC の出力対象端末。</summary>
    public XteinkDevice XtcDevice
    {
        get => _settings.XtcDevice;
        set
        {
            if (_settings.XtcDevice == value) return;
            _settings.XtcDevice = value;
            _settings.Save();
            OnChanged();
            OnChanged(nameof(XtcResolutionText));
        }
    }

    /// <summary>選択中の端末の解像度表示。</summary>
    public string XtcResolutionText
    {
        get
        {
            var (w, h) = _settings.XtcDevice.GetResolution();
            return $"{w} × {h} px";
        }
    }

    // フォント探索はファイルシステムを走査するので一度だけ行う。
    private readonly List<XtcFont> _fonts = [.. FontFinder.Enumerate()];

    /// <summary>描画に使えるフォントの一覧（検出したもの + ユーザーが指定したもの）。</summary>
    public IReadOnlyList<XtcFont> XtcFonts => _fonts;

    /// <summary>XTC 描画に使うフォント。null なら自動選択。</summary>
    public XtcFont? XtcFont
    {
        // 未設定のときは FontFinder が実際に選ぶフォントを見せる。
        get => _fonts.FirstOrDefault(f =>
                   string.Equals(f.FilePath, _settings.XtcFontFile, StringComparison.OrdinalIgnoreCase))
               ?? _fonts.FirstOrDefault();
        set
        {
            var path = value?.FilePath ?? "";
            if (string.Equals(_settings.XtcFontFile, path, StringComparison.OrdinalIgnoreCase)) return;
            _settings.XtcFontFile = path;
            _settings.Save();
            OnChanged();
        }
    }

    // ---- ダークモード ----
    public bool IsDarkMode
    {
        get => _settings.IsDarkMode;
        set
        {
            if (_settings.IsDarkMode == value) return;
            _settings.IsDarkMode = value;
            _settings.Save();
            ApplyTheme(value);
            OnChanged();
            OnChanged(nameof(DarkModeToggleLabel));
        }
    }
    public string DarkModeToggleLabel => _settings.IsDarkMode ? "☀ ライト" : "🌙 ダーク";

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

    // ---- 状態 ----
    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set { if (Set(ref _isBusy, value)) OnChanged(nameof(IsNotBusy)); } }
    public bool IsNotBusy => !IsBusy;

    private double _progressValue;
    public double ProgressValue { get => _progressValue; set => Set(ref _progressValue, value); }

    private bool _progressIndeterminate;
    public bool ProgressIndeterminate { get => _progressIndeterminate; set => Set(ref _progressIndeterminate, value); }

    private string _statusText = "URL、または作品タイトルを入力して「ライブラリに追加」。1行に1件で、まとめて追加できます。";
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    // ---- コマンド ----
    public AsyncRelayCommand AddCommand { get; }
    public AsyncRelayCommand CheckAllCommand { get; }
    public AsyncRelayCommand UpdateAllCommand { get; }
    public RelayCommand ChooseFolderCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncRelayCommand<LibraryItemVm> CheckItemCommand { get; }
    public AsyncRelayCommand<LibraryItemVm> UpdateItemCommand { get; }
    public AsyncRelayCommand<LibraryItemVm> ChangeCoverCommand { get; }
    public RelayCommand<LibraryItemVm> OpenEpubCommand { get; }
    public RelayCommand<LibraryItemVm> OpenFolderCommand { get; }
    public RelayCommand<LibraryItemVm> RemoveCommand { get; }
    public RelayCommand<LibraryItemVm> RenameCommand { get; }
    public RelayCommand OpenProofreadingRulesCommand { get; }
    public RelayCommand<LibraryItemVm> CopyTitleCommand { get; }
    public RelayCommand<LibraryItemVm> OpenUrlCommand { get; }
    public RelayCommand ToggleDarkModeCommand { get; }
    public AsyncRelayCommand<LibraryItemVm> ConvertXtcCommand { get; }
    public AsyncRelayCommand ConvertAllXtcCommand { get; }
    public RelayCommand ChooseXtcFontCommand { get; }

    private EpubOptions Options => _settings.ToEpubOptions();

    private Progress<DownloadProgress> MakeDlProgress() => new(p =>
    {
        if (p.Total > 0) { ProgressIndeterminate = false; ProgressValue = 100.0 * p.Current / p.Total; }
        else ProgressIndeterminate = true;
        StatusText = $"[{p.Phase}] {p.Message}";
    });

    private async Task AddAsync()
    {
        var inputs = ParseInputs(_urlsText);
        if (inputs.Count == 0) { StatusText = "URLまたは作品タイトルを入力してください。"; return; }

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var dlProgress = MakeDlProgress();
        var buildProgress = new Progress<string>(m => StatusText = "[EPUB] " + m);
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

                    StatusText = $"ダウンロード中: {url}";
                    var novel = await _service.DownloadAsync(url, Options, dlProgress, _cts.Token);

                    var cover = await PickCoverAsync(novel);
                    if (cover.Cancelled) { skipped++; continue; }

                    StatusText = "EPUBを生成中…";
                    var entry = await Task.Run(() => _service.BuildAndRegister(novel, cover.Image, Options, buildProgress));
                    UpsertItem(entry);
                    added++;

                    // 暫定表紙で進めた場合は、差し替え候補を裏で集めておく。
                    if (cover.IsProvisional) PrefetchCoverCandidates(entry);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { failed++; lastError = ex.Message; }
            }

            UrlsText = "";
            StatusText = $"追加 {added} 件" + (skipped > 0 ? $" / スキップ {skipped}" : "")
                         + (failed > 0 ? $" / 失敗 {failed}（{lastError}）" : "") + "。";
        }
        catch (OperationCanceledException) { StatusText = $"中断しました（追加 {added} 件）。"; }
        finally { EndBusy(); }
    }

    /// <summary>入力がURLならそのまま、タイトルなら検索して目次URLに解決する。</summary>
    private async Task<string?> ResolveInputAsync(string input, CancellationToken token)
    {
        if (NovelSearchService.LooksLikeUrl(input))
        {
            var url = input.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? input : "https://" + input;
            if (_service.IsSupported(url)) return url;
            StatusText = $"対応していないURLです（なろう / カクヨムのみ）: {input}";
            return null;
        }

        StatusText = $"タイトルを検索中: {input}";
        var hits = await NovelSearchService.SearchAsync(input, 5, token);
        if (hits.Count == 0)
        {
            StatusText = $"「{input}」に一致する作品が見つかりませんでした。";
            return null;
        }

        var best = hits[0];
        var others = hits.Count > 1 ? $"（他 {hits.Count - 1} 件の候補あり）" : "";
        StatusText = $"「{input}」→ {best.Label} {others}";
        return best.Url;
    }

    /// <summary>暫定表紙で先へ進むか、ダイアログで選ばせるか。</summary>
    private async Task<(ScrapedImage? Image, bool IsProvisional, bool Cancelled)> PickCoverAsync(NovelDownload novel)
    {
        if (_settings.AutoCover)
            return (await Task.Run(() => CoverSelection.PickProvisional(novel, Options)), true, false);

        var dlg = new CoverPickerWindow(novel, Options) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return (null, false, true);
        return (dlg.Result?.Image, false, false);
    }

    /// <summary>
    /// 表紙候補を裏で集めてキャッシュしておく。追加処理は待たせない。
    /// 集め終わったら該当カードの「表紙」ボタンに印を付ける。
    /// </summary>
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
            catch { /* 候補が集まらなくても本処理には影響しない */ }
        });
    }

    private async Task CheckAllAsync()
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
                catch { item.Entry.Status = UpdateStatus.Error; }
                finally { item.IsBusy = false; item.Refresh(); }
            }
            StatusText = updates > 0
                ? $"更新チェック完了：{updates} 作品に更新があります。"
                : "更新チェック完了：すべて最新です。";
        }
        catch (OperationCanceledException) { StatusText = "更新チェックを中断しました。"; }
        finally { EndBusy(); }
    }

    private async Task UpdateAllAsync()
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
            StatusText = $"{done} 作品を更新しました。";
        }
        catch (OperationCanceledException) { StatusText = $"更新を中断しました（{done} 件完了）。"; }
        finally { EndBusy(); }
    }

    private async Task CheckItemAsync(LibraryItemVm? item)
    {
        if (item == null) return;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        item.IsBusy = true; item.BusyText = "確認中…";
        try
        {
            var has = await _service.CheckAsync(item.Entry, _cts.Token);
            StatusText = has ? $"「{item.Title}」に更新があります。" : $"「{item.Title}」は最新です。";
        }
        catch (OperationCanceledException) { StatusText = "中断しました。"; }
        catch (Exception ex) { item.Entry.Status = UpdateStatus.Error; StatusText = "確認失敗：" + ex.Message; }
        finally { item.IsBusy = false; item.Refresh(); EndBusy(); }
    }

    private async Task UpdateItemAsync(LibraryItemVm? item)
    {
        if (item == null) return;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        try { await UpdateOne(item); StatusText = $"「{item.Title}」を更新しました。"; }
        catch (OperationCanceledException) { StatusText = "更新を中断しました。"; }
        catch (Exception ex) { StatusText = "更新失敗：" + ex.Message; }
        finally { EndBusy(); }
    }

    private async Task UpdateOne(LibraryItemVm item)
    {
        item.IsBusy = true; item.BusyText = "更新中…";
        var dlProgress = MakeDlProgress();
        var buildProgress = new Progress<string>(m => StatusText = "[EPUB] " + m);
        try
        {
            await _service.UpdateAsync(item.Entry, dlProgress, buildProgress, _cts!.Token);
        }
        finally { item.IsBusy = false; item.Refresh(); }
    }

    private async Task ChangeCoverAsync(LibraryItemVm? item)
    {
        if (item == null) return;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        var dlProgress = MakeDlProgress();
        var buildProgress = new Progress<string>(m => StatusText = "[EPUB] " + m);
        item.IsBusy = true; item.BusyText = "再取得中…";
        try
        {
            StatusText = "表紙変更のため再取得中…";
            var novel = await _service.DownloadAsync(item.Entry.Url, item.Entry.Options, dlProgress, _cts.Token);
            item.IsBusy = false;

            // 追加時に裏で集めた候補があれば、検索を待たずにそのまま見せる。
            var prefetched = await Task.Run(() =>
                CoverCandidateCache.Load(_settings.OutputFolder, item.Entry.Site, item.Entry.WorkId));

            var dlg = new CoverPickerWindow(novel, item.Entry.Options, prefetched)
            {
                Owner = Application.Current.MainWindow,
            };
            if (dlg.ShowDialog() != true) { StatusText = "表紙変更をキャンセルしました。"; return; }
            var cover = dlg.Result?.Image;
            StatusText = "EPUBを再生成中…";
            var entry = await Task.Run(() => _service.BuildAndRegister(novel, cover, item.Entry.Options, buildProgress));
            item.Refresh();
            item.SetCoverCandidateCount(0);
            StatusText = $"「{entry.Title}」の表紙を変更しました。";
        }
        catch (OperationCanceledException) { StatusText = "中断しました。"; }
        catch (Exception ex) { StatusText = "表紙変更失敗：" + ex.Message; }
        finally { item.IsBusy = false; item.Refresh(); EndBusy(); }
    }

    /// <summary>一覧にないフォントファイルをダイアログで選ぶ。</summary>
    private void ChooseXtcFont()
    {
        var dlg = new OpenFileDialog
        {
            Title = "XTC描画に使うフォントを選択",
            Filter = "フォントファイル (*.ttf;*.ttc;*.otf;*.otc)|*.ttf;*.ttc;*.otf;*.otc|すべてのファイル (*.*)|*.*",
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts"),
        };
        if (dlg.ShowDialog() != true) return;

        _settings.XtcFontFile = dlg.FileName;
        _settings.Save();

        // 一覧にない場合は選択肢として足しておく。
        if (!_fonts.Any(f => string.Equals(f.FilePath, dlg.FileName, StringComparison.OrdinalIgnoreCase)))
            _fonts.Add(new XtcFont(Path.GetFileNameWithoutExtension(dlg.FileName), dlg.FileName, "指定"));

        OnChanged(nameof(XtcFonts));
        OnChanged(nameof(XtcFont));
        StatusText = $"XTCフォント: {Path.GetFileName(dlg.FileName)}";
    }

    // ---- XTC 変換 ----

    private async Task ConvertXtcAsync(LibraryItemVm? item)
    {
        if (item == null) return;

        var parts = GetEpubParts(item);
        if (parts.Count == 0) { StatusText = "EPUBファイルが見つかりません。先に更新してください。"; return; }

        IsBusy = true;
        _cts = new CancellationTokenSource();
        try
        {
            var pages = await ConvertPartsAsync(item, parts, _cts.Token);
            StatusText = $"XTC変換完了: {item.Title}（{parts.Count} ファイル / 計 {pages} ページ）";
        }
        catch (OperationCanceledException) { StatusText = "XTC変換をキャンセルしました。"; }
        catch (Exception ex) { StatusText = "XTC変換に失敗しました：" + ex.Message; }
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

    private async Task ConvertAllXtcAsync()
    {
        var targets = Items.Where(i => GetEpubParts(i).Count > 0).ToList();
        if (targets.Count == 0) { StatusText = "変換できるEPUBがありません。"; return; }

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
                    StatusText = $"{item.Title}: {ex.Message}";
                }
                finally { item.IsBusy = false; }
            }
            StatusText = $"XTC一括変換 完了: 成功 {done} / 失敗 {failed}";
        }
        catch (OperationCanceledException) { StatusText = $"XTC一括変換を中断しました（成功 {done}）。"; }
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

    /// <summary>1作品ぶんのEPUB（分割ぶんを含む）をXTCへ変換し、総ページ数を返す。</summary>
    private async Task<int> ConvertPartsAsync(LibraryItemVm item, IReadOnlyList<string> parts, CancellationToken token)
    {
        var options = XtcConversionUtility.BuildOptions(Options);
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
                StatusText = $"[XTC] {label}  {p.ChapterIndex}/{p.ChapterCount} 章  {p.PagesEmitted} ページ";
            });

            var result = await XtcConversionUtility.ConvertEpubToXtcAsync(epubPath, xtcPath, options, progress, token);
            totalPages += result.PageCount;
        }

        return totalPages;
    }

    /// <summary>実在するEPUBファイルのパスを列挙する。</summary>
    private static List<string> GetEpubParts(LibraryItemVm item)
    {
        var parts = item.Entry.EpubParts.Count > 0
            ? item.Entry.EpubParts
            : [item.Entry.EpubPath];
        return parts.Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p)).ToList();
    }

    private void OpenEpub(LibraryItemVm? item)
    {
        if (item == null) return;
        if (!File.Exists(item.Entry.EpubPath)) { StatusText = "EPUBファイルが見つかりません。"; return; }
        try { Process.Start(new ProcessStartInfo(item.Entry.EpubPath) { UseShellExecute = true }); }
        catch (Exception ex) { StatusText = "起動失敗：" + ex.Message; }
    }

    private void OpenFolder(LibraryItemVm? item)
    {
        if (item == null) return;
        var path = item.Entry.EpubPath;
        try
        {
            if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else Process.Start("explorer.exe", $"\"{_settings.OutputFolder}\"");
        }
        catch (Exception ex) { StatusText = "フォルダを開けません：" + ex.Message; }
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
        StatusText = $"「{item.Title}」を削除しました。";
    }

    private void Rename(LibraryItemVm? item)
    {
        if (item == null) return;
        try
        {
            _service.RenameFiles(item.Entry, item.NameTemplateEdit);
            item.Refresh();
            StatusText = $"「{item.Title}」のファイル名を変更しました。";
        }
        catch (Exception ex) { StatusText = "ファイル名変更失敗：" + ex.Message; }
    }

    private void OpenProofreadingRules()
    {
        var path = NovelToEink.Core.ProofreadingService.DefaultPath;
        _ = NovelToEink.Core.ProofreadingService.Load();
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { StatusText = "ルールファイルを開けません：" + ex.Message; }
    }

    private void CopyTitle(LibraryItemVm? item)
    {
        if (item == null) return;
        try
        {
            Clipboard.SetText(item.Title);
            StatusText = $"「{item.Title}」をクリップボードにコピーしました。";
        }
        catch (Exception ex) { StatusText = "コピー失敗：" + ex.Message; }
    }

    private void OpenUrl(LibraryItemVm? item)
    {
        if (item == null || string.IsNullOrEmpty(item.Url)) return;
        try { Process.Start(new ProcessStartInfo(item.Url) { UseShellExecute = true }); }
        catch (Exception ex) { StatusText = "ブラウザを開けません：" + ex.Message; }
    }

    private void ToggleDarkMode() => IsDarkMode = !IsDarkMode;

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
        OnChanged(nameof(OutputFolder));
        StatusText = $"保存先を変更しました：{dlg.FolderName}（{Items.Count} 作品を読み込み）";
    }

    private void ReloadItems()
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
            // 前回の起動で集めた表紙候補が残っていれば、印を復元する。
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

    private void EndBusy()
    {
        IsBusy = false;
        ProgressIndeterminate = false;
        ProgressValue = 0;
    }

    private static void ApplyTheme(bool dark)
    {
        var res = Application.Current.Resources;
        if (dark)
        {
            res["AppBg"]      = new SolidColorBrush(Color.FromRgb(0x1A, 0x1B, 0x1E));
            res["CardBg"]     = new SolidColorBrush(Color.FromRgb(0x2B, 0x2D, 0x31));
            res["Border"]     = new SolidColorBrush(Color.FromRgb(0x3F, 0x42, 0x47));
            res["Muted"]      = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
            res["Text"]       = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6));
            res["AccentBrush"]= new SolidColorBrush(Color.FromRgb(0x81, 0x8C, 0xF8));
            res["AccentBg"]   = new SolidColorBrush(Color.FromRgb(0x31, 0x2E, 0x81));
            res["Subtle"]     = new SolidColorBrush(Color.FromRgb(0x36, 0x39, 0x40));
        }
        else
        {
            res["AppBg"]      = new SolidColorBrush(Color.FromRgb(0xF4, 0xF5, 0xF7));
            res["CardBg"]     = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            res["Border"]     = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));
            res["Muted"]      = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
            res["Text"]       = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27));
            res["AccentBrush"]= new SolidColorBrush(Color.FromRgb(0x4F, 0x46, 0xE5));
            res["AccentBg"]   = new SolidColorBrush(Color.FromRgb(0xEE, 0xF0, 0xFF));
            res["Subtle"]     = new SolidColorBrush(Color.FromRgb(0xEE, 0xF0, 0xF3));
        }
    }

    /// <summary>
    /// 入力欄をURL／タイトルの一覧に分解する。
    /// 行を単位にし、URLを含む行だけ空白で分割する（タイトルは空白を含みうるため）。
    /// </summary>
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
