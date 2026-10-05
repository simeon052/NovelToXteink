using System.ComponentModel;
using System.IO;
using Microsoft.Win32;
using NovelToEink.Core;
using NovelToEink.Xtc;

namespace NovelToEink.App;

/// <summary>
/// XTC 出力設定のサブビューモデル。
/// デバイス選択、フォント、文字サイズ、余白などの設定を管理する。
/// </summary>
public sealed class XtcSettingsViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly List<XtcFont> _fonts;

    // ローカルフィールド（ref が必要なので）
    private int _xtcFontSize;
    private int _xtcTextThreshold;
    private int _xtcPaddingTop;
    private int _xtcPaddingBottom;
    private int _xtcPaddingLeft;
    private int _xtcPaddingRight;

    private string? _busyText;
    public string? BusyText { get => _busyText; set => Set(ref _busyText, value); }

    /// <summary>コンストラクタ。設定と通知コールバックを受け取る。</summary>
    public XtcSettingsViewModel(AppSettings settings, Action<string?> notifyPropertyChanged)
    {
        _settings = settings;
        // フォント探索はファイルシステムを走査するので一度だけ行う。
        _fonts = [.. FontFinder.Enumerate()];

        // ローカルフィールドを初期化
        _xtcFontSize = settings.XtcFontSize;
        _xtcTextThreshold = settings.XtcTextThreshold;
        _xtcPaddingTop = settings.XtcPaddingTop;
        _xtcPaddingBottom = settings.XtcPaddingBottom;
        _xtcPaddingLeft = settings.XtcPaddingLeft;
        _xtcPaddingRight = settings.XtcPaddingRight;

        Notify = notifyPropertyChanged;
    }

    private Action<string?> Notify { get; }

    // ---- デバイス ----

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
            Notify(nameof(XtcDevice));
            Notify(nameof(XtcResolutionText));
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

    /// <summary>描画に使えるフォントの一覧（検出したもの + ユーザーが指定したもの）。</summary>
    public IReadOnlyList<XtcFont> XtcFonts => _fonts;

    /// <summary>XTC 描画に使うフォント。null なら自動選択。</summary>
    public XtcFont? XtcFont
    {
        get => _fonts.FirstOrDefault(f =>
                   string.Equals(f.FilePath, _settings.XtcFontFile, StringComparison.OrdinalIgnoreCase))
              ?? _fonts.FirstOrDefault();
        set
        {
            var path = value?.FilePath ?? "";
            if (string.Equals(_settings.XtcFontFile, path, StringComparison.OrdinalIgnoreCase)) return;
            _settings.XtcFontFile = path;
            _settings.Save();
            Notify(nameof(XtcFont));
        }
    }

    /// <summary>本文の文字サイズ（px）。8〜200 に丸める。</summary>
    public int XtcFontSize
    {
        get => _xtcFontSize;
        set => SettingsBinding.BindClamped(ref _xtcFontSize, value, 8, 200,
            () => { _settings.XtcFontSize = _xtcFontSize; _settings.Save(); }, Notify);
    }

    /// <summary>本文の 2 値化しきい値。128〜250 に丸める。</summary>
    public int XtcTextThreshold
    {
        get => _xtcTextThreshold;
        set => SettingsBinding.BindClamped(ref _xtcTextThreshold, value, 128, 250,
            () => { _settings.XtcTextThreshold = _xtcTextThreshold; _settings.Save(); }, Notify);
    }

    /// <summary>上余白（px）。</summary>
    public int XtcPaddingTop
    {
        get => _xtcPaddingTop;
        set => SettingsBinding.BindClampedMin(ref _xtcPaddingTop, value, 0,
            () => { _settings.XtcPaddingTop = _xtcPaddingTop; _settings.Save(); }, Notify);
    }

    /// <summary>下余白（px）。</summary>
    public int XtcPaddingBottom
    {
        get => _xtcPaddingBottom;
        set => SettingsBinding.BindClampedMin(ref _xtcPaddingBottom, value, 0,
            () => { _settings.XtcPaddingBottom = _xtcPaddingBottom; _settings.Save(); }, Notify);
    }

    /// <summary>左余白（px）。</summary>
    public int XtcPaddingLeft
    {
        get => _xtcPaddingLeft;
        set => SettingsBinding.BindClampedMin(ref _xtcPaddingLeft, value, 0,
            () => { _settings.XtcPaddingLeft = _xtcPaddingLeft; _settings.Save(); }, Notify);
    }

    /// <summary>右余白（px）。</summary>
    public int XtcPaddingRight
    {
        get => _xtcPaddingRight;
        set => SettingsBinding.BindClampedMin(ref _xtcPaddingRight, value, 0,
            () => { _settings.XtcPaddingRight = _xtcPaddingRight; _settings.Save(); }, Notify);
    }

    /// <summary>一覧にないフォントファイルをダイアログで選ぶ。</summary>
    public void ChooseXtcFont(Action<string?> statusMessage)
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

        Notify(nameof(XtcFonts));
        Notify(nameof(XtcFont));
        statusMessage($"XTCフォント: {Path.GetFileName(dlg.FileName)}");
    }
}
