using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using NovelToEink.Core;
using NovelToEink.Xtc;

namespace NovelToEink.App;

public sealed class MainViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly LibraryService _service;

    /// <summary>XTC 出力設定のサブビューモデル。</summary>
    public XtcSettingsViewModel XtcSettings { get; }

    /// <summary>EPUB 生成設定のサブビューモデル。</summary>
    public EpubSettingsViewModel EpubSettings { get; }

    /// <summary>ライブラリ操作（追加・更新・削除・ソート・表紙選択・XTC変換）のサブビューモデル。</summary>
    public LibrarySectionViewModel Library { get; }

    /// <summary>ライブラリと設定のエクスポート/インポート操作のサブビューモデル。</summary>
    public ImportExportSectionViewModel ImportExport { get; }

    public MainViewModel()
    {
        _settings = AppSettings.Load();
        _service = new LibraryService(_settings);

        // 設定サブビューモデルを初期化
        XtcSettings = new XtcSettingsViewModel(_settings);
        EpubSettings = new EpubSettingsViewModel(_settings);

        // ライブラリセクションを初期化
        Library = new LibrarySectionViewModel(_settings, _service)
        {
            EpubSettings = EpubSettings,
            StatusSetter = msg => { StatusText = msg; },
        };

        // インポート/エクスポートセクションを初期化
        ImportExport = new ImportExportSectionViewModel(_settings, _service)
        {
            BusySetter = v => Library.SetIsBusy(v),
            StatusSetter = msg => { StatusText = msg; },
            EpubSettings = EpubSettings,
            XtcSettings = XtcSettings,
            ThemeApplier = ApplyTheme,
            DarkModeSetter = v => { _settings.IsDarkMode = v; OnChanged(nameof(IsDarkMode)); },
            DarkModeChangedNotifier = () => OnChanged(nameof(IsDarkMode)),
            DarkModeLabelChangedNotifier = () => OnChanged(nameof(DarkModeToggleLabel)),
            LibraryReloader = () => Library.ReloadItems(),
        };

        ApplyTheme(_settings.IsDarkMode);

        // コマンドを初期化してサブビューモデルに渡す
        var addCmd = new AsyncRelayCommand(Library.AddAsync, () => !Library.IsBusy && Library.HasInputs);
        var checkAllCmd = new AsyncRelayCommand(Library.CheckAllAsync, () => !Library.IsBusy && Library.Items.Count > 0);
        var updateAllCmd = new AsyncRelayCommand(Library.UpdateAllAsync, () => !Library.IsBusy && Library.Items.Any(i => i.HasUpdate));
        var checkItemCmd = new AsyncRelayCommand<LibraryItemVm>(Library.CheckItemAsync, _ => !Library.IsBusy);
        var updateItemCmd = new AsyncRelayCommand<LibraryItemVm>(Library.UpdateItemAsync, _ => !Library.IsBusy);
        var changeCoverCmd = new AsyncRelayCommand<LibraryItemVm>(Library.ChangeCoverAsync, _ => !Library.IsBusy);
        var pickPendingCmd = new AsyncRelayCommand(Library.PickPendingCoversAsync, () => !Library.IsBusy && Library.PendingCoverCount > 0);
        var openEpubCmd = new RelayCommand<LibraryItemVm>(Library.OpenEpub);
        var openFolderCmd = new RelayCommand<LibraryItemVm>(Library.OpenFolder);
        var convertXtcCmd = new AsyncRelayCommand<LibraryItemVm>(Library.ConvertXtcAsync, _ => !Library.IsBusy);
        var convertAllXtcCmd = new AsyncRelayCommand(Library.ConvertAllXtcAsync, () => !Library.IsBusy && Library.Items.Count > 0);

        Library.InitCommands(
            addCmd,
            checkAllCmd,
            updateAllCmd,
            checkItemCmd,
            updateItemCmd,
            changeCoverCmd,
            pickPendingCmd,
            openEpubCmd,
            openFolderCmd,
            convertXtcCmd,
            convertAllXtcCmd);

        // インポート/エクスポートコマンド
        ImportExport.ExportLibraryCommand = new AsyncRelayCommand(ImportExport.ExportLibraryAsync, () => !Library.IsBusy);
        ImportExport.ImportLibraryCommand = new AsyncRelayCommand(ImportExport.ImportLibraryAsync, () => !Library.IsBusy);

        // XTC フォント選択
        ChooseXtcFontCommand = new RelayCommand(ChooseXtcFont, () => !Library.IsBusy);

        // 校正ルール・ダークモード切替（コンストラクタ内で初期化）
        OpenProofreadingRulesCommand = new RelayCommand(OpenProofreadingRules);
        ToggleDarkModeCommand = new RelayCommand(ToggleDarkMode);
    }

    // ---- 入力・オプション ----

    public string UrlsText
    {
        get => Library.GetUrlsText();
        set => Library.SetUrlsText(value);
    }

    /// <summary>URLまたはタイトルが1件以上入力されているか。</summary>
    public bool HasInputs => Library.HasInputs;

    public string OutputFolder => _settings.OutputFolder;

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

    // ---- 状態 ----

    public bool IsBusy => Library.IsBusy;
    public bool IsNotBusy => Library.IsNotBusy;

    public double ProgressValue => Library.ProgressValue;
    public bool ProgressIndeterminate => Library.ProgressIndeterminate;

    private string _statusText = "URL、または作品タイトルを入力して「ライブラリに追加」。1行に1件で、まとめて追加できます。";
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    // ---- コマンド（公開） ----

    /// <summary>ライブラリに追加。</summary>
    public AsyncRelayCommand AddCommand => Library.AddCommand!;

    /// <summary>すべて更新チェック。</summary>
    public AsyncRelayCommand CheckAllCommand => Library.CheckAllCommand!;

    /// <summary>すべて更新を適用。</summary>
    public AsyncRelayCommand UpdateAllCommand => Library.UpdateAllCommand!;

    /// <summary>保存先フォルダ選択。</summary>
    public RelayCommand ChooseFolderCommand => Library.ChooseFolderCommand!;

    /// <summary>処理キャンセル。</summary>
    public RelayCommand CancelCommand => Library.CancelCommand!;

    /// <summary>作品を更新チェック。</summary>
    public AsyncRelayCommand<LibraryItemVm> CheckItemCommand => Library.CheckItemCommand!;

    /// <summary>作品を更新。</summary>
    public AsyncRelayCommand<LibraryItemVm> UpdateItemCommand => Library.UpdateItemCommand!;

    /// <summary>表紙を変更。</summary>
    public AsyncRelayCommand<LibraryItemVm> ChangeCoverCommand => Library.ChangeCoverCommand!;

    /// <summary>表紙をまとめて選ぶ。</summary>
    public AsyncRelayCommand PickPendingCoversCommand => Library.PickPendingCoversCommand!;

    /// <summary>EPUBを開く。</summary>
    public RelayCommand<LibraryItemVm> OpenEpubCommand => Library.OpenEpubCommand!;

    /// <summary>フォルダを開く。</summary>
    public RelayCommand<LibraryItemVm> OpenFolderCommand => Library.OpenFolderCommand!;

    /// <summary>削除。</summary>
    public RelayCommand<LibraryItemVm> RemoveCommand => Library.RemoveCommand!;

    /// <summary>ファイル名を変更。</summary>
    public RelayCommand<LibraryItemVm> RenameCommand => Library.RenameCommand!;

    /// <summary>校正ルールファイルを開く。</summary>
    public RelayCommand OpenProofreadingRulesCommand { get; private set; }

    /// <summary>タイトルをコピー。</summary>
    public RelayCommand<LibraryItemVm> CopyTitleCommand => Library.CopyTitleCommand!;

    /// <summary>URLを開く。</summary>
    public RelayCommand<LibraryItemVm> OpenUrlCommand => Library.OpenUrlCommand!;

    /// <summary>ダークモード切替。</summary>
    public RelayCommand ToggleDarkModeCommand { get; private set; }

    /// <summary>ライブラリをエクスポート。</summary>
    public AsyncRelayCommand ExportLibraryCommand => ImportExport.ExportLibraryCommand!;

    /// <summary>ライブラリをインポート。</summary>
    public AsyncRelayCommand ImportLibraryCommand => ImportExport.ImportLibraryCommand!;

    /// <summary>XTC変換。</summary>
    public AsyncRelayCommand<LibraryItemVm> ConvertXtcCommand => Library.ConvertXtcCommand!;

    /// <summary>XTC一括変換。</summary>
    public AsyncRelayCommand ConvertAllXtcCommand => Library.ConvertAllXtcCommand!;

    /// <summary>XTCフォント選択。</summary>
    public RelayCommand ChooseXtcFontCommand { get; }

    private EpubOptions Options => EpubSettings.ToEpubOptions();

    // ---- 公共プロパティ（UIバインド用） ----

    public ObservableCollection<LibraryItemVm> Items => Library.Items;

    /// <summary>並べ替え：変換日。</summary>
    public bool IsSortConverted { get => Library.IsSortConverted; set => Library.IsSortConverted = value; }

    /// <summary>並べ替え：サイト更新日。</summary>
    public bool IsSortSiteUpdated { get => Library.IsSortSiteUpdated; set => Library.IsSortSiteUpdated = value; }

    /// <summary>並べ替え：タイトル。</summary>
    public bool IsSortTitle { get => Library.IsSortTitle; set => Library.IsSortTitle = value; }

    // ---- メソッド ----

    private void OpenProofreadingRules()
    {
        var path = NovelToEink.Core.ProofreadingService.DefaultPath;
        _ = NovelToEink.Core.ProofreadingService.Load();
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { StatusText = "ルールファイルを開けません：" + ex.Message; }
    }

    private void ToggleDarkMode() => IsDarkMode = !IsDarkMode;

    private void ChooseXtcFont() => XtcSettings.ChooseXtcFont(s => { if (s != null) StatusText = s; });

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
            res["DangerBg"]   = new SolidColorBrush(Color.FromRgb(0x4A, 0x22, 0x26));
            res["DangerText"] = new SolidColorBrush(Color.FromRgb(0xFC, 0xA5, 0xA5));
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
            res["DangerBg"]   = new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2));
            res["DangerText"] = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C));
        }
    }
}
