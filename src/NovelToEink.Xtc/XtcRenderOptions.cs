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

    private int? _rubyFontSize;

    /// <summary>
    /// ルビの文字サイズ（px）。明示しなければ <see cref="FontSize"/> から算出する
    /// （既定の 36px に対して従来どおり 14px になる比率）。
    /// </summary>
    public int RubyFontSize
    {
        get => _rubyFontSize ?? Math.Max(6, (int)Math.Round(FontSize * 14.0 / 36.0));
        set => _rubyFontSize = value;
    }

    private int? _lineSpacing;

    /// <summary>
    /// 行の送り幅（px）。縦組みでは列と列の間隔。
    /// 明示しなければ <see cref="FontSize"/> から算出する（既定の 36px に対して従来どおり 56px）。
    /// 文字サイズを変えたときに送りが追随しないと列同士が重なって読めなくなるため、
    /// 明示値であっても文字サイズ + 2px は必ず確保する。
    /// </summary>
    public int LineSpacing
    {
        get => Math.Max(FontSize + 2, _lineSpacing ?? (int)Math.Round(FontSize * 14.0 / 9.0));
        set => _lineSpacing = value;
    }

    /// <summary>上マージン（px）。</summary>
    public int MarginTop { get; set; } = 12;

    /// <summary>下マージン（px）。</summary>
    public int MarginBottom { get; set; } = 12;

    /// <summary>右マージン（px）。縦組みの書き出し位置に効く。</summary>
    public int MarginRight { get; set; } = 12;

    /// <summary>左マージン（px）。ここを下回ると改ページする。</summary>
    public int MarginLeft { get; set; } = 12;

    /// <summary>
    /// 利用者が指定する追加の余白（px）。上の Margin* に加算される。
    /// Margin* は端末端での欠けを避けるための最低限の下駄で、こちらは好みで広げるためのもの。
    /// </summary>
    public (int Top, int Bottom, int Left, int Right) Padding { get; set; } = (Top: 3, Bottom: 0, Left: 0, Right: 0);

    /// <summary>上端から本文までの実効余白（px）。</summary>
    public int EffectiveTop => MarginTop + Padding.Top;

    /// <summary>下端から本文までの実効余白（px）。</summary>
    public int EffectiveBottom => MarginBottom + Padding.Bottom;

    /// <summary>左端から本文までの実効余白（px）。</summary>
    public int EffectiveLeft => MarginLeft + Padding.Left;

    /// <summary>右端から本文までの実効余白（px）。</summary>
    public int EffectiveRight => MarginRight + Padding.Right;

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
