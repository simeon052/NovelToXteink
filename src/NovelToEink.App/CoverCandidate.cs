using System.IO;
using System.Windows.Media.Imaging;
using NovelToEink.Core;

namespace NovelToEink.App;

/// <summary>表紙候補（スクレイピング画像／生成画像／表紙なし）。</summary>
public sealed class CoverCandidate
{
    public ScrapedImage? Image { get; }
    public bool IsNone { get; }
    public string Label { get; }
    public BitmapSource? Thumbnail { get; }

    private CoverCandidate(ScrapedImage? image, bool isNone, string label, BitmapSource? thumb)
    {
        Image = image;
        IsNone = isNone;
        Label = label;
        Thumbnail = thumb;
    }

    public static CoverCandidate None() => new(null, true, "表紙なし", null);

    /// <summary>
    /// previewOptions を渡すとプレビューにグレースケール＋リサイズを適用する（EPUB生成と同じ見た目を確認できる）。
    /// </summary>
    public static CoverCandidate FromImage(ScrapedImage img, string label, EpubOptions? previewOptions = null)
    {
        BitmapSource? thumb;
        if (previewOptions?.GrayscaleImages == true)
        {
            try
            {
                var previewOpts = new EpubOptions
                {
                    GrayscaleImages = true,
                    MaxImageDimension = 600,
                    CoverMaxDimension = 600,
                    JpegQuality = 80,
                };
                var processed = ImageProcessor.EncodeForEpub(img.Data, previewOpts, isCover: true);
                thumb = MakeThumb(processed);
            }
            catch
            {
                thumb = MakeThumb(img.Data);
            }
        }
        else
        {
            thumb = MakeThumb(img.Data);
        }
        return new CoverCandidate(img, false, label, thumb);
    }

    private static BitmapSource MakeThumb(byte[] data)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.DecodePixelWidth = 180;
        bmp.StreamSource = new MemoryStream(data);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
