using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using NovelToEink.Core;

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

        AddCommand = new AsyncRelayCommand(AddAsync, () => !IsBusy && HasUrls);
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
        ExportLibraryCommand = new AsyncRelayCommand(ExportLibraryAsync, () => !IsBusy);
        ImportLibraryCommand = new AsyncRelayCommand(ImportLibraryAsync, () => !IsBusy);
    }

    // ---- 入力・オプション ----
    private string _urlsText = "";
    public string UrlsText
    {
        get => _urlsText;
        set { if (Set(ref _urlsText, value)) OnChanged(nameof(HasUrls)); }
    }
    public bool HasUrls => ParseUrls(_urlsText).Count > 0;

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

    private string _statusText = "URLを貼り付けて「ライブラリに追加」。複数URLは改行/スペース区切りでまとめて追加できます。";
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
    public AsyncRelayCommand ExportLibraryCommand { get; }
    public AsyncRelayCommand ImportLibraryCommand { get; }

    private EpubOptions Options => _settings.ToEpubOptions();

    private Progress<DownloadProgress> MakeDlProgress() => new(p =>
    {
        if (p.Total > 0) { ProgressIndeterminate = false; ProgressValue = 100.0 * p.Current / p.Total; }
        else ProgressIndeterminate = true;
        StatusText = $"[{p.Phase}] {p.Message}";
    });

    private async Task AddAsync()
    {
        var urls = ParseUrls(_urlsText);
        var supported = urls.Where(_service.IsSupported).ToList();
        if (supported.Count == 0) { StatusText = "対応URLが見つかりません（なろう / カクヨムのみ）。"; return; }

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var dlProgress = MakeDlProgress();
        var buildProgress = new Progress<string>(m => StatusText = "[EPUB] " + m);
        int added = 0, skipped = 0, failed = 0;
        string? lastError = null;

        try
        {
            foreach (var url in supported)
            {
                _cts.Token.ThrowIfCancellationRequested();
                try
                {
                    StatusText = $"ダウンロード中: {url}";
                    var novel = await _service.DownloadAsync(url, Options, dlProgress, _cts.Token);

                    var dlg = new CoverPickerWindow(novel, Options) { Owner = Application.Current.MainWindow };
                    if (dlg.ShowDialog() != true) { skipped++; continue; }
                    var cover = dlg.Result?.Image;

                    StatusText = "EPUBを生成中…";
                    var entry = await Task.Run(() => _service.BuildAndRegister(novel, cover, Options, buildProgress));
                    UpsertItem(entry);
                    added++;
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
            var dlg = new CoverPickerWindow(novel, item.Entry.Options) { Owner = Application.Current.MainWindow };
            if (dlg.ShowDialog() != true) { StatusText = "表紙変更をキャンセルしました。"; return; }
            var cover = dlg.Result?.Image;
            StatusText = "EPUBを再生成中…";
            var entry = await Task.Run(() => _service.BuildAndRegister(novel, cover, item.Entry.Options, buildProgress));
            item.Refresh();
            StatusText = $"「{entry.Title}」の表紙を変更しました。";
        }
        catch (OperationCanceledException) { StatusText = "中断しました。"; }
        catch (Exception ex) { StatusText = "表紙変更失敗：" + ex.Message; }
        finally { item.IsBusy = false; item.Refresh(); EndBusy(); }
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
            Items.Add(new LibraryItemVm(e));

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

    private static List<string> ParseUrls(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return text.Split([' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();
    }

    private async Task ExportLibraryAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "ライブラリをエクスポート",
            Filter = "JSON ファイル (*.json)|*.json|すべてのファイル (*.*)|*.*",
            DefaultExt = ".json",
            FileName = $"library_export_{DateTime.Now:yyyyMMdd_HHmmss}.json"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                IsBusy = true;
                StatusText = "ライブラリをエクスポート中...";
                await Task.Run(() => _service.ExportLibrary(dialog.FileName));
                StatusText = $"✓ ライブラリをエクスポートしました: {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                StatusText = $"✗ エクスポート失敗: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    private async Task ImportLibraryAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "ライブラリをインポート",
            Filter = "JSON ファイル (*.json)|*.json|すべてのファイル (*.*)|*.*",
            DefaultExt = ".json"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                IsBusy = true;
                StatusText = "ライブラリをインポート中...";

                var (importedLibrary, importedSettings) = await Task.Run(() => _service.ImportLibrary(dialog.FileName));

                var result = MessageBox.Show(
                    $"インポートする項目: {importedLibrary.Count} 作品\n\n" +
                    "現在のライブラリに追加しますか？\n" +
                    "（既存の作品は更新されます）",
                    "ライブラリインポート確認",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // インポートしたエントリを現在のライブラリにマージ
                    foreach (var imported in importedLibrary)
                    {
                        var existing = _service.Entries.FirstOrDefault(e => e.Url == imported.Url);
                        if (existing != null)
                        {
                            // 既存エントリを更新
                            existing.Title = imported.Title;
                            existing.Author = imported.Author;
                            existing.EpisodeCount = imported.EpisodeCount;
                            existing.IsCompleted = imported.IsCompleted;
                            existing.SiteLastUpdated = imported.SiteLastUpdated;
                            existing.Status = UpdateStatus.Unknown;
                        }
                        else
                        {
                            // 新規追加: 移行元環境の絶対パスや状態を持ち込まない
                            _service.Entries.Add(new LibraryEntry
                            {
                                Url = imported.Url,
                                Site = imported.Site,
                                WorkId = imported.WorkId,
                                Title = imported.Title,
                                Author = imported.Author,
                                Description = imported.Description,
                                EpisodeCount = imported.EpisodeCount,
                                LastEpisodeTitle = imported.LastEpisodeTitle,
                                EpubPath = "",
                                EpubParts = [],
                                NameTemplate = string.IsNullOrWhiteSpace(imported.NameTemplate) ? NameFormatter.DefaultTemplate : imported.NameTemplate,
                                IsCompleted = imported.IsCompleted,
                                SiteLastUpdated = imported.SiteLastUpdated,
                                CoverImagePath = null,
                                Options = imported.Options ?? new EpubOptions(),
                                AddedAt = DateTimeOffset.Now,
                                LastCheckedAt = null,
                                LastUpdatedAt = null,
                                Status = UpdateStatus.Unknown,
                            });
                        }
                    }
                    _service.Persist();

                    // 設定をインポート（確認ダイアログ）
                    var applySettings = MessageBox.Show(
                        "設定もインポートしますか？",
                        "設定インポート確認",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (applySettings == MessageBoxResult.Yes)
                    {
                        LibraryService.ApplyImportedSettings(_settings, importedSettings);
                        _settings.Save();
                        ApplyTheme(_settings.IsDarkMode);
                        OnChanged(nameof(Vertical));
                        OnChanged(nameof(GrayscaleImages));
                        OnChanged(nameof(IncludeInlineImages));
                        OnChanged(nameof(KeepRuby));
                        OnChanged(nameof(EnableProofreading));
                        OnChanged(nameof(EpisodesPerFile));
                        OnChanged(nameof(RequestDelayMs));
                        OnChanged(nameof(IsDarkMode));
                        OnChanged(nameof(DarkModeToggleLabel));
                    }

                    ReloadItems();
                    StatusText = $"✓ ライブラリをインポートしました ({importedLibrary.Count} 作品)";
                }
            }
            catch (Exception ex)
            {
                StatusText = $"✗ インポート失敗: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
