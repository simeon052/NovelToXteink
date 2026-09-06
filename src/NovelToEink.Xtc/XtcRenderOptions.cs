namespace NovelToEink.Xtc;

/// <summary>EPUB → XTC 変換のレンダリング設定。</summary>
public sealed class XtcRenderOptions
{
    /// <summary>出力対象の端末。解像度はここから決まる。</summary>
    public XteinkDevice Device { get; set; } = XteinkDevice.X4Pro;

    /// <summary>本文フォントのファイルパス。空なら <see cref="FontFinder"/> が既定の日本語フォントを探す。</summary>
    public string? FontFile { get; set; }

    /// <summary>本文の文字サイズ（px）。</summary>
    public int FontSize { get; set; } = 36;

    /// <summary>ルビの文字サイズ（px）。</summary>
    public int RubyFontSize { get; set; } = 14;

    /// <summary>行の送り幅（px）。縦組みでは列と列の間隔。</summary>
    public int LineSpacing { get; set; } = 56;

    /// <summary>上マージン（px）。</summary>
    public int MarginTop { get; set; } = 12;

    /// <summary>下マージン（px）。</summary>
    public int MarginBottom { get; set; } = 12;

    /// <summary>右マージン（px）。縦組みの書き出し位置に効く。</summary>
    public int MarginRight { get; set; } = 12;

    /// <summary>左マージン（px）。ここを下回ると改ページする。</summary>
    public int MarginLeft { get; set; } = 12;

    /// <summary>縦組みにするか。false なら横組み（左→右）。</summary>
    public bool Vertical { get; set; } = true;

    /// <summary>挿絵を出力に含めるか。</summary>
    public bool IncludeImages { get; set; } = true;

    /// <summary>ルビを描画するか。</summary>
    public bool RenderRuby { get; set; } = true;

    /// <summary>本文ページの 2 値化しきい値。大きいほど文字が太くなる。</summary>
    public int TextThreshold { get; set; } = 200;

    /// <summary>挿絵に Floyd–Steinberg ディザリングを適用するか。false ならしきい値処理。</summary>
    public bool DitherImages { get; set; } = true;

    /// <summary>挿絵をしきい値処理するときのしきい値（<see cref="DitherImages"/> が false のとき）。</summary>
    public int ImageThreshold { get; set; } = 128;

    /// <summary>白黒反転（ナイトモード）。挿絵には適用しない。</summary>
    public bool NightMode { get; set; }

    /// <summary>解像度（px）。<see cref="Device"/> から導出される。</summary>
    public (int Width, int Height) Resolution => Device.GetResolution();

    /// <summary>読み進む向き。縦組みなら右→左。</summary>
    public XtcReadDirection ReadDirection =>
        Vertical ? XtcReadDirection.RightToLeft : XtcReadDirection.LeftToRight;
}
