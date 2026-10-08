using NovelToEink.Core;

namespace NovelToEink.App;

/// <summary>
/// EPUB 生成設定のサブビューモデル。
/// 縦書き、モノクロ化、ルビ保持、校正、分割数、待機時間などの設定を扱う。
/// 値は <see cref="AppSettings"/> だけが持ち、ここではコピーを持たない。
/// </summary>
public sealed class EpubSettingsViewModel : ViewModelBase
{
    private readonly AppSettings _settings;

    public EpubSettingsViewModel(AppSettings settings) => _settings = settings;

    /// <summary>EPUB（と XTC）の生成オプション。</summary>
    public EpubOptions ToEpubOptions() => _settings.ToEpubOptions();

    /// <summary>縦書き出力する。</summary>
    public bool Vertical
    {
        get => _settings.Vertical;
        set { if (SettingsBinding.Write(_settings, _settings.Vertical, value, v => _settings.Vertical = v)) OnChanged(); }
    }

    /// <summary>画像をモノクロ化して2値化する。</summary>
    public bool GrayscaleImages
    {
        get => _settings.GrayscaleImages;
        set { if (SettingsBinding.Write(_settings, _settings.GrayscaleImages, value, v => _settings.GrayscaleImages = v)) OnChanged(); }
    }

    /// <summary>本文内の埋め込み画像もEPUBに含める。</summary>
    public bool IncludeInlineImages
    {
        get => _settings.IncludeInlineImages;
        set { if (SettingsBinding.Write(_settings, _settings.IncludeInlineImages, value, v => _settings.IncludeInlineImages = v)) OnChanged(); }
    }

    /// <summary>ルビ情報を保持する。</summary>
    public bool KeepRuby
    {
        get => _settings.KeepRuby;
        set { if (SettingsBinding.Write(_settings, _settings.KeepRuby, value, v => _settings.KeepRuby = v)) OnChanged(); }
    }

    /// <summary>API リクエスト間の待機時間（ms）。</summary>
    public int RequestDelayMs
    {
        get => _settings.RequestDelayMs;
        set { if (SettingsBinding.Write(_settings, _settings.RequestDelayMs, value, v => _settings.RequestDelayMs = v)) OnChanged(); }
    }

    /// <summary>1つのEPUBファイルに含む話数。</summary>
    public int EpisodesPerFile
    {
        get => _settings.EpisodesPerFile;
        set { if (SettingsBinding.Write(_settings, _settings.EpisodesPerFile, value, v => _settings.EpisodesPerFile = v)) OnChanged(); }
    }

    /// <summary>テキスト校正ルールを適用する。</summary>
    public bool EnableProofreading
    {
        get => _settings.EnableProofreading;
        set { if (SettingsBinding.Write(_settings, _settings.EnableProofreading, value, v => _settings.EnableProofreading = v)) OnChanged(); }
    }

    /// <summary>追加時に表紙選択で止まらず、暫定表紙で先へ進むか。</summary>
    public bool AutoCover
    {
        get => _settings.AutoCover;
        set { if (SettingsBinding.Write(_settings, _settings.AutoCover, value, v => _settings.AutoCover = v)) OnChanged(); }
    }

    /// <summary>設定が外部から差し替わったとき（インポート）に、すべての項目の再読み込みを画面へ伝える。</summary>
    public void NotifyAllChanged() => OnChanged(string.Empty);
}
