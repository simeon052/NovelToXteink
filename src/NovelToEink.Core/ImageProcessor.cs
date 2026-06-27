using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NovelToEink.Core;

/// <summary>画像のデコード・E-Ink 向け再エンコード・テキスト表紙生成。</summary>
public static class ImageProcessor
{
    private static readonly string[] CjkFontCandidates =
    [
        "Yu Gothic", "Yu Gothic UI", "Meiryo", "MS Gothic", "MS PGothic",
        "BIZ UDGothic", "Noto Sans CJK JP", "Noto Sans JP", "Yu Mincho", "MS Mincho",
    ];

    /// <summary>バイト列から画像情報を読み取り ScrapedImage を作る。デコード不能なら null。</summary>
    public static ScrapedImage? TryLoad(byte[] data, string url, int episodeIndex = 0, bool isOfficialCover = false)
    {
        try
        {
            var info = Image.Identify(data);
            if (info is null || info.Width <= 0 || info.Height <= 0) return null;
            return new ScrapedImage
            {
                SourceUrl = url,
                Data = data,
                Width = info.Width,
                Height = info.Height,
                EpisodeIndex = episodeIndex,
                IsOfficialCover = isOfficialCover,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>EPUB 収録用に再エンコード（リサイズ＋任意でグレースケール）し JPEG を返す。</summary>
    /// <param name="part">分冊番号（1始まり）。partCount > 1 のとき表紙に通し番号バッジを合成する。</param>
    /// <param name="partCount">総分冊数。1 以下の場合はスタンプしない。</param>
    public static byte[] EncodeForEpub(byte[] src, EpubOptions opt, bool isCover, int part = 0, int partCount = 0)
    {
        var maxDim = isCover ? opt.CoverMaxDimension : opt.MaxImageDimension;
        using var image = Image.Load(src);
        Resize(image, maxDim);
        if (opt.GrayscaleImages)
            image.Mutate(x => x.Grayscale());
        if (isCover && part > 0 && partCount > 1)
        {
            try { StampPartLabel(image, part, partCount); }
            catch { /* フォント未使用環境などはスタンプを省略 */ }
        }
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms, new JpegEncoder { Quality = Math.Clamp(opt.JpegQuality, 1, 100) });
        return ms.ToArray();
    }

    /// <summary>表紙画像の右下に「N/M」バッジを描画する（グレースケール変換後に呼ぶこと）。</summary>
    private static void StampPartLabel(Image image, int part, int partCount)
    {
        var families = ResolveCjkFamilies();
        FontFamily primary;
        if (families.Count > 0)
            primary = families[0];
        else if (SystemFonts.Families.Any())
            primary = SystemFonts.Families.First();
        else
            return;

        var text = $"{part}/{partCount}";
        var fontSize = Math.Max(18f, image.Width * 0.08f);
        var font = primary.CreateFont(fontSize, FontStyle.Bold);
        var fallbacks = families.Count > 1 ? families.Skip(1).ToArray() : [];

        // 右下基準で配置
        var textOpts = new RichTextOptions(font)
        {
            Origin = new PointF(image.Width - 12f, image.Height - 12f),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            FallbackFontFamilies = fallbacks,
        };

        var bounds = TextMeasurer.MeasureBounds(text, textOpts);
        var pad = fontSize * 0.3f;
        var bgRect = new SixLabors.ImageSharp.Drawing.RectangularPolygon(
            bounds.Left - pad,
            bounds.Top - pad,
            bounds.Width + pad * 2,
            bounds.Height + pad * 2);

        image.Mutate(ctx =>
        {
            // 半透明黒背景
            ctx.Fill(Color.FromRgba(0, 0, 0, 210), bgRect);
            // 白テキスト
            ctx.DrawText(textOpts, text, Color.White);
        });
    }

    private static void Resize(Image image, int maxDim)
    {
        if (maxDim <= 0) return;
        var longest = Math.Max(image.Width, image.Height);
        if (longest <= maxDim) return;
        var scale = (double)maxDim / longest;
        var w = Math.Max(1, (int)Math.Round(image.Width * scale));
        var h = Math.Max(1, (int)Math.Round(image.Height * scale));
        image.Mutate(x => x.Resize(w, h));
    }

    /// <summary>タイトル・作者からシンプルなテキスト表紙(JPEG)を生成する。</summary>
    public static ScrapedImage GenerateTextCover(string title, string author, EpubOptions opt)
    {
        const int w = 800, h = 1200;
        using var image = new Image<Rgb24>(w, h, Color.White.ToPixel<Rgb24>());

        var families = ResolveCjkFamilies();
        var primary = families.Count > 0 ? families[0] : SystemFonts.Families.First();
        var fallbacks = families.Skip(1).ToArray();

        var titleFont = primary.CreateFont(54, FontStyle.Bold);
        var authorFont = primary.CreateFont(34, FontStyle.Regular);

        image.Mutate(ctx =>
        {
            // 枠線
            ctx.Draw(Color.Black, 3f, new SixLabors.ImageSharp.Drawing.RectangularPolygon(30, 30, w - 60, h - 60));

            var titleOpts = new RichTextOptions(titleFont)
            {
                Origin = new PointF(w / 2f, h * 0.40f),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                WrappingLength = w - 160,
                LineSpacing = 1.3f,
                FallbackFontFamilies = fallbacks,
            };
            ctx.DrawText(titleOpts, title, Color.Black);

            if (!string.IsNullOrWhiteSpace(author))
            {
                var authorOpts = new RichTextOptions(authorFont)
                {
                    Origin = new PointF(w / 2f, h * 0.82f),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    WrappingLength = w - 160,
                    FallbackFontFamilies = fallbacks,
                };
                ctx.DrawText(authorOpts, author, Color.Black);
            }
        });

        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms, new JpegEncoder { Quality = 85 });
        return new ScrapedImage
        {
            SourceUrl = "generated:cover",
            Data = ms.ToArray(),
            Width = w,
            Height = h,
            EpisodeIndex = 0,
            IsOfficialCover = false,
        };
    }

    /// <summary>フォントが使えない環境向けの最小限の表紙（枠のみ白地）。</summary>
    public static ScrapedImage GenerateMinimalCover()
    {
        const int w = 800, h = 1200;
        using var image = new Image<Rgb24>(w, h, Color.White.ToPixel<Rgb24>());
        image.Mutate(ctx => ctx.Draw(Color.Black, 3f,
            new SixLabors.ImageSharp.Drawing.RectangularPolygon(30, 30, w - 60, h - 60)));
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms, new JpegEncoder { Quality = 85 });
        return new ScrapedImage { SourceUrl = "generated:minimal", Data = ms.ToArray(), Width = w, Height = h };
    }

    private static List<FontFamily> ResolveCjkFamilies()
    {
        var found = new List<FontFamily>();
        foreach (var name in CjkFontCandidates)
        {
            if (SystemFonts.TryGet(name, out var fam))
                found.Add(fam);
        }
        return found;
    }
}
