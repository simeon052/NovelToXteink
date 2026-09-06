using SkiaSharp;

namespace NovelToEink.Xtc;

/// <summary>挿絵をページサイズのグレースケールへ落とし込むヘルパー。</summary>
internal static class ImageDithering
{
    /// <summary>
    /// 画像をページに収まるよう縮小し、白地の中央に配置したグレースケール画素列を返す。
    /// </summary>
    public static byte[]? FitToPage(byte[] imageData, int pageWidth, int pageHeight)
    {
        using var bitmap = SKBitmap.Decode(imageData);
        if (bitmap is null) return null;

        var scale = Math.Min((float)pageWidth / bitmap.Width, (float)pageHeight / bitmap.Height);
        // 拡大はしない。元画像が小さいときはそのままの大きさで中央に置く。
        scale = Math.Min(scale, 1f);

        var targetWidth = Math.Max(1, (int)(bitmap.Width * scale));
        var targetHeight = Math.Max(1, (int)(bitmap.Height * scale));

        var info = new SKImageInfo(pageWidth, pageHeight, SKColorType.Gray8, SKAlphaType.Opaque);
        using var page = new SKBitmap(info);
        using (var canvas = new SKCanvas(page))
        {
            canvas.Clear(SKColors.White);
            var destination = SKRect.Create(
                (pageWidth - targetWidth) / 2f,
                (pageHeight - targetHeight) / 2f,
                targetWidth,
                targetHeight);
            using var paint = new SKPaint { IsAntialias = true };
            using var image = SKImage.FromBitmap(bitmap);
            canvas.DrawImage(image, destination, new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        }

        var pixels = page.GetPixelSpan();
        var gray = new byte[pageWidth * pageHeight];
        for (var y = 0; y < pageHeight; y++)
            pixels.Slice(y * page.RowBytes, pageWidth).CopyTo(gray.AsSpan(y * pageWidth, pageWidth));

        return gray;
    }

    /// <summary>
    /// Floyd–Steinberg 誤差拡散でグレースケールを 2 値（0 / 255）へ落とす。
    /// 写真調の挿絵は単純なしきい値処理より階調が残る。
    /// </summary>
    public static void FloydSteinberg(byte[] gray, int width, int height)
    {
        // 誤差が 8bit の範囲を超えるので float で持つ。
        var buffer = new float[gray.Length];
        for (var i = 0; i < gray.Length; i++) buffer[i] = gray[i];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                var oldValue = buffer[index];
                var newValue = oldValue < 128f ? 0f : 255f;
                buffer[index] = newValue;
                var error = oldValue - newValue;

                if (x + 1 < width) buffer[index + 1] += error * 7f / 16f;
                if (y + 1 < height)
                {
                    if (x > 0) buffer[index + width - 1] += error * 3f / 16f;
                    buffer[index + width] += error * 5f / 16f;
                    if (x + 1 < width) buffer[index + width + 1] += error * 1f / 16f;
                }
            }
        }

        for (var i = 0; i < gray.Length; i++)
            gray[i] = (byte)Math.Clamp(buffer[i], 0f, 255f);
    }
}
