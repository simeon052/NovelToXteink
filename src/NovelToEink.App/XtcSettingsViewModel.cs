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

    private string? _busyText;
    public string? BusyText { get => _busyText; set => Set(ref _busyText, value); }

    public XtcSettingsViewModel(AppSettings settings)
    {
        _settings = settings;
        // フォント探索はファイルシステムを走査するので一度だけ行う。
        _fonts = [.. FontFinder.Enumerate()];
    }

    /// <summary>EPUB 生成後に XTC も作るか。</summary>
    public bool GenerateXtc
    {
        get => _settings.GenerateXtc;
        set { if (SettingsBinding.Write(_settings, _settings.GenerateXtc, value, v => _settings.GenerateXtc = v)) OnChanged(); }
    }

    // ---- デバイス ----

    /// <summary>選択できる端末の一覧。</summary>
    public IReadOnlyList<XteinkDevice> XtcDevices { get; } = [XteinkDevice.X3, XteinkDevice.X4Pro];

    /// <summary>XTC の出力対象端末。</summary>
    public XteinkDevice XtcDevice
    {
        get => _settings.XtcDevice;
        set
        {
            if (!SettingsBinding.Write(_settings, _settings.XtcDevice, value, v => _settings.XtcDevice = v)) return;
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
            SettingsBinding.Write(_settings, _settings.XtcFontFile, path, v => _settings.XtcFontFile = v);
            OnChanged();
        }
    }

    /// <summary>文字サイズ（px）の下限・上限。入力はこの範囲に丸める。</summary>
    public const int MinFontSize = 8, MaxFontSize = 200;

    /// <summary>本文の 2 値化しきい値の下限・上限。入力はこの範囲に丸める。</summary>
    public const int MinTextThreshold = 128, MaxTextThreshold = 250;

    /// <summary>本文の文字サイズ（px）。<see cref="MinFontSize"/>〜<see cref="MaxFontSize"/> に丸める。</summary>
    public int XtcFontSize
    {
        get => _settings.XtcFontSize;
        set { if (SettingsBinding.WriteClamped(_settings, _settings.XtcFontSize, value, MinFontSize, MaxFontSize, v => _settings.XtcFontSize = v)) OnChanged(); }
    }

    /// <summary>本文の 2 値化しきい値。<see cref="MinTextThreshold"/>〜<see cref="MaxTextThreshold"/> に丸める。</summary>
    public int XtcTextThreshold
    {
        get => _settings.XtcTextThreshold;
        set { if (SettingsBinding.WriteClamped(_settings, _settings.XtcTextThreshold, value, MinTextThreshold, MaxTextThreshold, v => _settings.XtcTextThreshold = v)) OnChanged(); }
    }

    /// <summary>上余白（px）。</summary>
    public int XtcPaddingTop
    {
        get => _settings.XtcPaddingTop;
        set { if (SettingsBinding.WriteMin(_settings, _settings.XtcPaddingTop, value, 0, v => _settings.XtcPaddingTop = v)) OnChanged(); }
    }

    /// <summary>下余白（px）。</summary>
    public int XtcPaddingBottom
    {
        get => _settings.XtcPaddingBottom;
        set { if (SettingsBinding.WriteMin(_settings, _settings.XtcPaddingBottom, value, 0, v => _settings.XtcPaddingBottom = v)) OnChanged(); }
    }

    /// <summary>左余白（px）。</summary>
    public int XtcPaddingLeft
    {
        get => _settings.XtcPaddingLeft;
        set { if (SettingsBinding.WriteMin(_settings, _settings.XtcPaddingLeft, value, 0, v => _settings.XtcPaddingLeft = v)) OnChanged(); }
    }

    /// <summary>右余白（px）。</summary>
    public int XtcPaddingRight
    {
        get => _settings.XtcPaddingRight;
        set { if (SettingsBinding.WriteMin(_settings, _settings.XtcPaddingRight, value, 0, v => _settings.XtcPaddingRight = v)) OnChanged(); }
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

        OnChanged(nameof(XtcFonts));
        OnChanged(nameof(XtcFont));
        statusMessage($"XTCフォント: {Path.GetFileName(dlg.FileName)}");
    }

    /// <summary>設定が外部から差し替わったとき（インポート）に、すべての項目の再読み込みを画面へ伝える。</summary>
    public void NotifyAllChanged() => OnChanged(string.Empty);
}
