namespace NovelToEink.Xtc;

/// <summary>変換の進捗。</summary>
/// <param name="ChapterIndex">処理中の章（0 始まり）。</param>
/// <param name="ChapterCount">総章数。</param>
/// <param name="PagesEmitted">これまでに生成したページ数。</param>
public readonly record struct XtcProgress(int ChapterIndex, int ChapterCount, int PagesEmitted);

/// <summary>変換結果。</summary>
/// <param name="OutputPath">生成された .xtc のパス。</param>
/// <param name="PageCount">総ページ数。</param>
/// <param name="Device">出力対象の端末。</param>
/// <param name="Width">ページ幅（px）。</param>
/// <param name="Height">ページ高さ（px）。</param>
public sealed record XtcResult(string OutputPath, int PageCount, XteinkDevice Device, int Width, int Height);

/// <summary>EPUB を実機で読めるバイナリ XTC（1bit モノクロ）へ変換する。</summary>
public static class EpubToXtc
{
    /// <summary>EPUB を XTC へ変換する。</summary>
    /// <param name="epubPath">入力 EPUB のパス。</param>
    /// <param name="outputPath">出力 .xtc のパス。</param>
    /// <param name="options">レンダリング設定。null なら既定（X4 Pro・縦組み）。</param>
    /// <param name="progress">進捗通知。</param>
    /// <param name="cancellationToken">キャンセル用トークン。</param>
    public static XtcResult Convert(
        string epubPath,
        string outputPath,
        XtcRenderOptions? options = null,
        IProgress<XtcProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(epubPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!File.Exists(epubPath))
            throw new FileNotFoundException($"EPUB が見つかりません: {epubPath}", epubPath);

        options ??= new XtcRenderOptions();
        var (width, height) = options.Resolution;

        var (_, chapters) = EpubContent.Read(epubPath);
        if (chapters.Count == 0)
            throw new InvalidDataException($"EPUB から本文を取り出せませんでした: {epubPath}");

        using var builder = new XtcBuilder();
        using (var renderer = new XtcPageRenderer(options, (blob, w, h) => builder.AddPage(blob, w, h)))
        {
            for (var i = 0; i < chapters.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                renderer.RenderChapter(chapters[i]);
                progress?.Report(new XtcProgress(i + 1, chapters.Count, builder.PageCount));
            }
            renderer.Finish();
        }

        if (builder.PageCount == 0)
            throw new InvalidDataException($"ページが 1 枚も生成されませんでした: {epubPath}");

        builder.Complete(outputPath, options.ReadDirection);
        return new XtcResult(outputPath, builder.PageCount, options.Device, width, height);
    }

    /// <summary>EPUB を XTC へ変換する（非同期）。</summary>
    public static Task<XtcResult> ConvertAsync(
        string epubPath,
        string outputPath,
        XtcRenderOptions? options = null,
        IProgress<XtcProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => Convert(epubPath, outputPath, options, progress, cancellationToken), cancellationToken);
}
