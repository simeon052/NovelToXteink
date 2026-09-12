using SkiaSharp;

namespace NovelToEink.Xtc;

/// <summary>
/// 1 ページ分のグレースケール描画面。描き終わったら 1bpp へ 2 値化して XTG 化する。
/// </summary>
internal sealed class PageCanvas : IDisposable
{
    private readonly SKBitmap _bitmap;
    private readonly SKCanvas _canvas;

    public int Width { get; }
    public int Height { get; }

    /// <summary>何か描かれたか。空ページを出力しないための判定に使う。</summary>
    public bool IsDirty { get; private set; }

    public SKCanvas Canvas
    {
        get
        {
            IsDirty = true;
            return _canvas;
        }
    }

    public PageCanvas(int width, int height)
    {
        Width = width;
        Height = height;
        _bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque));
        _canvas = new SKCanvas(_bitmap);
        _canvas.Clear(SKColors.White);
    }

    /// <summary>白紙に戻す。</summary>
    public void Reset()
    {
        _canvas.Clear(SKColors.White);
        IsDirty = false;
    }

    /// <summary>現在の内容をグレースケール画素列として取り出す。</summary>
    public byte[] ToGrayscale(bool invert)
    {
        var pixels = _bitmap.GetPixelSpan();
        var gray = new byte[Width * Height];
        var rowBytes = _bitmap.RowBytes;

        for (var y = 0; y < Height; y++)
        {
            var src = pixels.Slice(y * rowBytes, Width);
            var dst = gray.AsSpan(y * Width, Width);
            src.CopyTo(dst);
        }

        if (invert)
        {
            for (var i = 0; i < gray.Length; i++) gray[i] = (byte)(255 - gray[i]);
        }

        return gray;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _canvas.Dispose();
        _bitmap.Dispose();
    }
}
