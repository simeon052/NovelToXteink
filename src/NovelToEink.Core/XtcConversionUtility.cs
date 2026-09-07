using NovelToEink.Xtc;
using NovelToEink.XtcConverter;

namespace NovelToEink.Core;

/// <summary>
/// XTC変換ユーティリティ。EPUB を Xteink 端末で読めるバイナリ XTC（1bit モノクロ）へ変換する。
/// </summary>
public static class XtcConversionUtility
{
    /// <summary>
    /// EPUBファイルをXTCファイルに変換する
    /// </summary>
    /// <param name="epubPath">EPUBファイルパス</param>
    /// <param name="xtcPath">出力XTCファイルパス</param>
    /// <param name="options">変換オプション</param>
    public static void ConvertEpubToXtc(string epubPath, string xtcPath, XtcRenderOptions options)
    {
        try
        {
            var xtcDir = Path.GetDirectoryName(xtcPath);
            if (!string.IsNullOrEmpty(xtcDir)) Directory.CreateDirectory(xtcDir);

            new X4ProXtcConverter(options).ConvertEpubToXtc(epubPath, xtcPath, options);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"XTC変換中にエラーが発生しました: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// EPUBファイルをXTCファイルに変換する（非同期・進捗通知つき）。
    /// </summary>
    /// <param name="epubPath">EPUBファイルパス</param>
    /// <param name="xtcPath">出力XTCファイルパス</param>
    /// <param name="options">変換オプション</param>
    /// <param name="progress">進捗通知</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    public static async Task<XtcResult> ConvertEpubToXtcAsync(
        string epubPath, string xtcPath, XtcRenderOptions options,
        IProgress<XtcProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var xtcDir = Path.GetDirectoryName(xtcPath);
        if (!string.IsNullOrEmpty(xtcDir)) Directory.CreateDirectory(xtcDir);

        return await EpubToXtc.ConvertAsync(epubPath, xtcPath, options, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary><see cref="EpubOptions"/> から XTC 変換オプションを組み立てる。</summary>
    public static XtcRenderOptions BuildOptions(EpubOptions epubOptions) => new()
    {
        Device = epubOptions.XtcDevice,
        Vertical = epubOptions.WritingMode == WritingMode.Vertical,
        FontFile = string.IsNullOrWhiteSpace(epubOptions.XtcFontFile) ? null : epubOptions.XtcFontFile,
        RenderRuby = epubOptions.KeepRuby,
        IncludeImages = epubOptions.IncludeInlineImages,
        FontSize = epubOptions.XtcFontSize,
        // 要素名で明示的に対応付ける。位置だけで渡すと上下左右が入れ替わっても気付けない。
        Padding = (
            Top: epubOptions.XtcPaddingTop,
            Bottom: epubOptions.XtcPaddingBottom,
            Left: epubOptions.XtcPaddingLeft,
            Right: epubOptions.XtcPaddingRight),
    };

    /// <summary>
    /// XTCファイルの生成を実行する
    /// </summary>
    /// <param name="epubPath">EPUBファイルパス</param>
    /// <param name="epubOptions">EPUBオプション</param>
    public static void GenerateXtcFile(string epubPath, EpubOptions epubOptions)
    {
        if (!epubOptions.GenerateXtc) return;

        try
        {
            var xtcPath = Path.ChangeExtension(epubPath, ".xtc");
            ConvertEpubToXtc(epubPath, xtcPath, BuildOptions(epubOptions));
            Console.WriteLine($"XTCファイル生成完了: {xtcPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"XTC生成に失敗しました: {ex.Message}");
        }
    }
}
