using NovelToEink.Xtc;

namespace NovelToEink.Core;

/// <summary>本文の組み方向。</summary>
public enum WritingMode
{
    /// <summary>横書き（左→右）。</summary>
    Horizontal,
    /// <summary>縦書き（右→左）。日本語の小説向け。</summary>
    Vertical,
}

/// <summary>
/// EPUB 生成オプション。Xteink X3 のような非力な E-Ink 端末向けに
/// 「軽い」EPUB を作ることを既定値とする。
/// </summary>
public sealed class EpubOptions
{
    /// <summary>組み方向。既定は縦書き（日本語小説の標準）。</summary>
    public WritingMode WritingMode { get; set; } = WritingMode.Vertical;

    /// <summary>ルビ（&lt;ruby&gt;）を残すか。端末が未対応なら false にすると親文字のみ残す。</summary>
    public bool KeepRuby { get; set; } = true;

    /// <summary>挿絵を本文に含めるか。容量・メモリ削減のため false も可。</summary>
    public bool IncludeInlineImages { get; set; } = true;

    /// <summary>画像をグレースケール化する（E-Ink 端末・容量削減向け）。</summary>
    public bool GrayscaleImages { get; set; } = true;

    /// <summary>画像の最大辺ピクセル。これを超える画像は縮小する。</summary>
    public int MaxImageDimension { get; set; } = 1200;

    /// <summary>表紙画像の最大辺ピクセル。</summary>
    public int CoverMaxDimension { get; set; } = 1200;

    /// <summary>JPEG 品質（1-100）。</summary>
    public int JpegQuality { get; set; } = 82;

    /// <summary>本文の基準フォントサイズ（em ではなく % 指定）。</summary>
    public int BaseFontPercent { get; set; } = 100;

    /// <summary>言語コード。</summary>
    public string Language { get; set; } = "ja";

    /// <summary>1EPUBあたりの最大話数。これを超えると分割する。0以下で分割しない。</summary>
    public int EpisodesPerFile { get; set; } = 200;

    /// <summary>テキスト校正を有効にする（%AppData%\NovelToEink\proofreading.json のルールを適用）。</summary>
    public bool EnableProofreading { get; set; } = true;
    /// <summary>
    /// XTCファイルを同時に生成するか
    /// </summary>
    public bool GenerateXtc { get; set; } = false;

    /// <summary>
    /// XTC変換用のフォントファイル（空文字列の場合は自動選択）
    /// </summary>
    public string XtcFontFile { get; set; } = "";

    /// <summary>XTC の出力対象端末。解像度はここで決まる（X3: 528x792 / X4 Pro: 480x800）。</summary>
    public XteinkDevice XtcDevice { get; set; } = XteinkDevice.X4Pro;
    
    /// <summary>XTC 本文の文字サイズ（px）。</summary>
    public int XtcFontSize { get; set; } = 36;

    /// <summary>XTC 本文の 2 値化しきい値。大きいほど線が太くなる。</summary>
    public int XtcTextThreshold { get; set; } = 200;

    // ValueTuple は System.Text.Json の既定設定では永続化されない。
    // EpubOptions は library.json に保存されるので int 4 本で持つ。
    /// <summary>XTC の上余白（px）。既定 3px。</summary>
    public int XtcPaddingTop { get; set; } = 3;

    /// <summary>XTC の下余白（px）。</summary>
    public int XtcPaddingBottom { get; set; }

    /// <summary>XTC の左余白（px）。</summary>
    public int XtcPaddingLeft { get; set; }

    /// <summary>XTC の右余白（px）。</summary>
    public int XtcPaddingRight { get; set; }
}

/// <summary>ダウンロード時の挙動。</summary>
public sealed class DownloadOptions
{
    /// <summary>各リクエスト間の待機ミリ秒（サーバ負荷・規約配慮）。</summary>
    public int RequestDelayMs { get; set; } = 1500;

    /// <summary>取得する最大エピソード数（0以下で無制限）。動作確認用に制限できる。</summary>
    public int MaxEpisodes { get; set; } = 0;

    /// <summary>画像（挿絵・表紙候補）をダウンロードするか。</summary>
    public bool DownloadImages { get; set; } = true;
}
