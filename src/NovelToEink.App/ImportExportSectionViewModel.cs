using System.IO;
using System.Windows;
using Microsoft.Win32;
using NovelToEink.Core;

namespace NovelToEink.App;

/// <summary>
/// ライブラリと設定のエクスポート/インポート操作のサブビューモデル。
/// </summary>
public sealed class ImportExportSectionViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly LibraryService _service;

    /// <summary>MainViewModelのIsBusy更新用コールバック。</summary>
    public Action<bool>? BusySetter { get; set; }

    /// <summary>MainViewModelのStatusText更新用コールバック。</summary>
    public Action<string>? StatusSetter { get; set; }

    /// <summary>EpubSettingsViewModelへの参照（インポート時に再同期するため）。</summary>
    public EpubSettingsViewModel? EpubSettings { get; set; }

    /// <summary>XtcSettingsViewModelへの参照（インポート時に再同期するため）。</summary>
    public XtcSettingsViewModel? XtcSettings { get; set; }

    /// <summary>MainViewModelのApplyThemeコールバック。</summary>
    public Action<bool>? ThemeApplier { get; set; }

    /// <summary>MainViewModelのIsDarkMode更新用コールバック。</summary>
    public Action<bool>? DarkModeSetter { get; set; }

    /// <summary>MainViewModelのOnChanged('IsDarkMode')呼び出し用。</summary>
    public Action? DarkModeChangedNotifier { get; set; }

    /// <summary>MainViewModelのOnChanged('DarkModeToggleLabel')呼び出し用。</summary>
    public Action? DarkModeLabelChangedNotifier { get; set; }

    /// <summary>MainViewModelのReloadItemsコールバック。</summary>
    public Action? LibraryReloader { get; set; }

    public ImportExportSectionViewModel(AppSettings settings, LibraryService service)
    {
        _settings = settings;
        _service = service;
    }

    // ---- コマンド（MainViewModelで初期化） ----

    public AsyncRelayCommand? ExportLibraryCommand { get; set; }
    public AsyncRelayCommand? ImportLibraryCommand { get; set; }

    /// <summary>エクスポート実行可否。</summary>
    public bool CanExport => true; // IsBusy チェックは MainViewModel で行う

    /// <summary>インポート実行可否。</summary>
    public bool CanImport => true; // IsBusy チェックは MainViewModel で行う

    // ---- エクスポート ----

    public async Task ExportLibraryAsync()
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
                BusySetter?.Invoke(true);
                StatusSetter?.Invoke("ライブラリをエクスポート中...");
                await Task.Run(() => _service.ExportLibrary(dialog.FileName));
                StatusSetter?.Invoke($"✓ ライブラリをエクスポートしました: {Path.GetFileName(dialog.FileName)}");
            }
            catch (Exception ex)
            {
                StatusSetter?.Invoke($"✗ エクスポート失敗: {ex.Message}");
            }
            finally
            {
                BusySetter?.Invoke(false);
            }
        }
    }

    // ---- インポート ----

    public async Task ImportLibraryAsync()
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
                BusySetter?.Invoke(true);
                StatusSetter?.Invoke("ライブラリをインポート中...");

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
                            existing.Title = imported.Title;
                            existing.Author = imported.Author;
                            existing.EpisodeCount = imported.EpisodeCount;
                            existing.IsCompleted = imported.IsCompleted;
                            existing.SiteLastUpdated = imported.SiteLastUpdated;
                            existing.Status = UpdateStatus.Unknown;
                        }
                        else
                        {
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

                        // 各サブビューモデルの全項目を再読み込みさせる
                        EpubSettings?.NotifyAllChanged();
                        XtcSettings?.NotifyAllChanged();

                        ThemeApplier?.Invoke(_settings.IsDarkMode);
                        DarkModeSetter?.Invoke(_settings.IsDarkMode);
                        DarkModeChangedNotifier?.Invoke();
                        DarkModeLabelChangedNotifier?.Invoke();
                    }

                    LibraryReloader?.Invoke();
                    StatusSetter?.Invoke($"✓ ライブラリをインポートしました ({importedLibrary.Count} 作品)");
                }
            }
            catch (Exception ex)
            {
                StatusSetter?.Invoke($"✗ インポート失敗: {ex.Message}");
            }
            finally
            {
                BusySetter?.Invoke(false);
            }
        }
    }
}
