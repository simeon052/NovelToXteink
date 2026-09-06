using SkiaSharp;

namespace NovelToEink.Xtc;

/// <summary>
/// EPUB のノード列を 1bit ページ（XTG ブロック）へ組版する。
/// 縦組みは右上から下へ字を送り、行末で 1 列ぶん左へ移動する。
/// </summary>
public sealed class XtcPageRenderer : IDisposable
{
    private readonly XtcRenderOptions _options;
    private readonly SKTypeface _typeface;
    private readonly SKFont _bodyFont;
    private readonly SKFont _rubyFont;
    private readonly SKPaint _inkPaint;
    private readonly PageCanvas _page;
    private readonly int _pageWidth;
    private readonly int _pageHeight;

    private readonly Action<byte[], int, int> _emitPage;

    private float _cursorX;
    private float _cursorY;
    private bool _disposed;

    /// <summary>組版した総ページ数。</summary>
    public int PagesEmitted { get; private set; }

    /// <param name="options">レンダリング設定。</param>
    /// <param name="emitPage">ページが 1 枚仕上がるたびに呼ばれる（XTG ブロック, 幅, 高さ）。</param>
    /// <param name="bundledFontDirectory">同梱フォントの探索先。null なら既定の場所。</param>
    public XtcPageRenderer(XtcRenderOptions options, Action<byte[], int, int> emitPage,
        string? bundledFontDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(emitPage);

        _options = options;
        _emitPage = emitPage;
        (_pageWidth, _pageHeight) = options.Resolution;

        _typeface = FontFinder.Load(options.FontFile, bundledFontDirectory);
        _bodyFont = new SKFont(_typeface, options.FontSize) { Edging = SKFontEdging.Antialias, Subpixel = false };
        _rubyFont = new SKFont(_typeface, options.RubyFontSize) { Edging = SKFontEdging.Antialias, Subpixel = false };
        _inkPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };

        _page = new PageCanvas(_pageWidth, _pageHeight);
        ResetCursor();
    }

    /// <summary>1 章ぶんのノード列を組版する。章の終わりでは改ページする。</summary>
    public void RenderChapter(IReadOnlyList<EpubNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node.Kind)
            {
                case EpubNodeKind.Text:
                    DrawRun(node.Text, ruby: null);
                    break;

                case EpubNodeKind.Ruby:
                    DrawRun(node.Text, _options.RenderRuby && node.Ruby.Length > 0 ? node.Ruby : null);
                    break;

                case EpubNodeKind.Image when _options.IncludeImages:
                    DrawImage(node.ImageData!);
                    break;

                case EpubNodeKind.LineBreak:
                    NewLine();
                    break;
            }
        }

        // 章はページ境界で切る。読み手にとって章が混ざらないほうが自然。
        FlushPage();
    }

    /// <summary>書きかけのページがあれば出力する。全章の描画後に呼ぶ。</summary>
    public void Finish() => FlushPage();

    private void DrawRun(string text, string? ruby)
    {
        var runStartX = _cursorX;
        var runStartY = _cursorY;
        var glyphCount = 0;

        var i = 0;
        while (i < text.Length)
        {
            var hanging = PrepareCellFor(text[i]);

            // 行や列をまたいだらルビの基準位置が合わなくなるので、そこから測り直す。
            if (glyphCount > 0 && Math.Abs(_cursorX - runStartX) > 0.5f)
            {
                if (ruby is not null) DrawRuby(ruby, runStartX, runStartY, glyphCount);
                ruby = null;
                runStartX = _cursorX;
                runStartY = _cursorY;
                glyphCount = 0;
            }
            else if (glyphCount == 0)
            {
                runStartX = _cursorX;
                runStartY = _cursorY;
            }

            // 「!?」「!!」のような感嘆符の連続は 1 マスに横並びで詰める。
            if (i + 1 < text.Length && IsExclamation(text[i]) && IsExclamation(text[i + 1]))
            {
                DrawCombinedPair(text[i], text[i + 1]);
                i += 2;
            }
            else
            {
                DrawGlyph(text[i]);
                i++;
            }

            glyphCount++;
            Advance();

            // ぶら下げた文字はその行の最後。次の文字から新しい行にする。
            if (hanging) NewLine();
        }

        if (ruby is not null && glyphCount > 0)
            DrawRuby(ruby, runStartX, runStartY, glyphCount);
    }

    private void DrawGlyph(char original)
    {
        var glyph = _options.Vertical ? TategakiGlyphMap.Translate(original) : original;

        if (_options.Vertical && glyph != original && !_bodyFont.ContainsGlyph(glyph))
        {
            // フォントが縦組み用の字形を持っていないときの代替。
            if (TategakiGlyphMap.CanRotateAsFallback(original))
            {
                DrawRotated(original);
                return;
            }

            // 句読点は回転すると読めないので、マスの右上へ寄せて縦組みらしく見せる。
            DrawAt(original, _cursorX + _options.FontSize * 0.5f, _cursorY - _options.FontSize * 0.22f);
            return;
        }

        DrawAt(glyph, _cursorX, _cursorY);
    }

    private void DrawAt(char c, float cellLeft, float cellTop)
    {
        var text = c.ToString();
        var width = _bodyFont.MeasureText(text);
        // 半角文字はマスの中央に置く。全角はマス幅と一致するので実質そのまま。
        var offsetX = Math.Max(0f, (_options.FontSize - width) / 2f);
        _page.Canvas.DrawText(text, cellLeft + offsetX, cellTop + BaselineOffset(_bodyFont), _bodyFont, _inkPaint);
    }

    private void DrawRotated(char c)
    {
        var canvas = _page.Canvas;
        var centerX = _cursorX + _options.FontSize / 2f;
        var centerY = _cursorY + _options.FontSize / 2f;

        canvas.Save();
        canvas.RotateDegrees(90f, centerX, centerY);

        var text = c.ToString();
        var width = _bodyFont.MeasureText(text);
        canvas.DrawText(text, centerX - width / 2f,
            centerY - (_bodyFont.Metrics.Ascent + _bodyFont.Metrics.Descent) / 2f, _bodyFont, _inkPaint);

        canvas.Restore();
    }

    private void DrawCombinedPair(char first, char second)
    {
        var size = _options.FontSize * 0.75f;
        using var font = new SKFont(_typeface, size) { Edging = SKFontEdging.Antialias, Subpixel = false };

        var half = _options.FontSize / 2f;
        var baseline = _cursorY + BaselineOffset(font) + (_options.FontSize - size) / 2f;

        DrawCentered(first, _cursorX, half, baseline, font);
        DrawCentered(second, _cursorX + half, half, baseline, font);
    }

    private void DrawCentered(char c, float left, float cellWidth, float baseline, SKFont font)
    {
        var text = c.ToString();
        var width = font.MeasureText(text);
        _page.Canvas.DrawText(text, left + (cellWidth - width) / 2f, baseline, font, _inkPaint);
    }

    private void DrawRuby(string ruby, float columnLeft, float baseTop, int baseGlyphCount)
    {
        var baseStep = _options.FontSize + 2f;
        var rubyStep = _options.RubyFontSize + 2f;
        var baseLength = baseGlyphCount * baseStep;
        var rubyLength = ruby.Length * rubyStep;

        // 親文字列の中央にルビ列の中央を合わせる。
        var y = baseTop + (baseLength - rubyLength) / 2f;
        var x = columnLeft + _options.FontSize + 4f;

        foreach (var c in ruby)
        {
            if (y >= _options.MarginTop && y < _pageHeight - _options.MarginBottom - _options.RubyFontSize)
                _page.Canvas.DrawText(c.ToString(), x, y + BaselineOffset(_rubyFont), _rubyFont, _inkPaint);
            y += rubyStep;
        }
    }

    private void DrawImage(byte[] imageData)
    {
        using var probe = SKBitmap.Decode(imageData);
        if (probe is null) return;

        var aspect = probe.Height > 0 ? (float)probe.Width / probe.Height : 1f;
        var isIllustration = probe.Height >= 400 || (aspect > 0.5f && probe.Height > _options.FontSize * 4);

        if (isIllustration)
        {
            // 挿絵は 1 ページ丸ごと使う。書きかけの本文があれば先に出す。
            FlushPage();
            var gray = ImageDithering.FitToPage(imageData, _pageWidth, _pageHeight);
            if (gray is null) return;

            if (_options.DitherImages)
            {
                ImageDithering.FloydSteinberg(gray, _pageWidth, _pageHeight);
                EmitGrayscale(gray, threshold: 128);
            }
            else
            {
                EmitGrayscale(gray, _options.ImageThreshold);
            }
            return;
        }

        // 外字・アイコンは 1 文字ぶんのマスに収める。
        PrepareCellFor('あ');
        var height = _options.FontSize;
        var width = Math.Max(1, (int)(probe.Width * ((float)height / probe.Height)));
        var destination = SKRect.Create(_cursorX + (_options.FontSize - width) / 2f, _cursorY, width, height);
        using var paint = new SKPaint { IsAntialias = true };
        using var image = SKImage.FromBitmap(probe);
        _page.Canvas.DrawImage(image, destination, new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        Advance();
    }

    private void Advance()
    {
        if (_options.Vertical) _cursorY += _options.FontSize + 2;
        else _cursorX += _options.FontSize + 2;
    }

    /// <summary>
    /// 次の 1 文字を置くマスを用意し、禁則処理を適用する。
    /// 戻り値が true なら、その文字は行末にぶら下げて置かれたので、描いた後に改行する。
    /// </summary>
    private bool PrepareCellFor(char next)
    {
        var overflows = _options.Vertical
            ? _cursorY > _pageHeight - _options.MarginBottom - _options.FontSize
            : _cursorX > _pageWidth - _options.MarginRight - _options.FontSize;

        if (overflows)
        {
            // 行頭禁則：句読点や閉じ括弧を行の頭に送らず、行末にぶら下げる。
            if (Kinsoku.IsForbiddenAtLineStart(next)) return true;

            NewLine();
            return false;
        }

        // 行末禁則：開き括弧が行の最後のマスに来るなら、先に改行してしまう。
        if (Kinsoku.IsForbiddenAtLineEnd(next) && IsLastCellOfLine())
            NewLine();

        return false;
    }

    private bool IsLastCellOfLine()
    {
        var step = _options.FontSize + 2;
        return _options.Vertical
            ? _cursorY + step > _pageHeight - _options.MarginBottom - _options.FontSize
            : _cursorX + step > _pageWidth - _options.MarginRight - _options.FontSize;
    }

    private void NewLine()
    {
        if (_options.Vertical)
        {
            // 行頭で改行指示が来た場合は空行を作らない。
            if (Math.Abs(_cursorY - _options.MarginTop) < 0.5f && _page.IsDirty) return;

            _cursorY = _options.MarginTop;
            _cursorX -= _options.LineSpacing;
            if (_cursorX < _options.MarginLeft) FlushPage();
        }
        else
        {
            if (Math.Abs(_cursorX - _options.MarginLeft) < 0.5f && _page.IsDirty) return;

            _cursorX = _options.MarginLeft;
            _cursorY += _options.LineSpacing;
            if (_cursorY > _pageHeight - _options.MarginBottom - _options.FontSize) FlushPage();
        }
    }

    private void FlushPage()
    {
        if (_page.IsDirty)
        {
            EmitGrayscale(_page.ToGrayscale(_options.NightMode), _options.TextThreshold);
            _page.Reset();
        }
        ResetCursor();
    }

    private void EmitGrayscale(byte[] gray, int threshold)
    {
        var blob = XtgWriter.FromGrayscale(gray, _pageWidth, _pageHeight, threshold);
        _emitPage(blob, _pageWidth, _pageHeight);
        PagesEmitted++;
    }

    private void ResetCursor()
    {
        if (_options.Vertical)
        {
            // 右端から書き始める。ルビ 1 列ぶんの余地を右に残しておく。
            _cursorX = _pageWidth - _options.FontSize - (_options.RubyFontSize + 4) - _options.MarginRight;
            _cursorY = _options.MarginTop;
        }
        else
        {
            _cursorX = _options.MarginLeft;
            _cursorY = _options.MarginTop;
        }
    }

    private static float BaselineOffset(SKFont font) => -font.Metrics.Ascent;

    private static bool IsExclamation(char c) => c is '！' or '？' or '!' or '?';

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _page.Dispose();
        _inkPaint.Dispose();
        _bodyFont.Dispose();
        _rubyFont.Dispose();
        _typeface.Dispose();
    }
}
