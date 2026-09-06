using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NovelToEink.Xtc;

namespace NovelToEink.XtcConverter
{
    /// <summary>
    /// EPUB を Xteink 端末向けのバイナリ XTC（1bit モノクロ）へ変換する。
    /// 実際の組版と書き出しは <see cref="NovelToEink.Xtc"/> ライブラリが行う。
    /// </summary>
    public class X4ProXtcConverter : XtcConverter
    {
        private readonly XtcOptions _options;

        /// <summary>既定オプションで変換器を作る。</summary>
        public X4ProXtcConverter(XtcOptions? options = null)
        {
            _options = options ?? new XtcOptions();
        }

        /// <summary>EPUB を XTC へ変換する。</summary>
        public void ConvertEpubToXtc(string epubPath, string outputPath)
            => ConvertEpubToXtc(epubPath, outputPath, _options);

        /// <summary>EPUB を XTC へ変換する。</summary>
        public override void ConvertEpubToXtc(string epubPath, string outputPath, XtcOptions? options)
        {
            options ??= _options;
            var (width, height) = options.Resolution;

            Console.WriteLine($"Converting EPUB to XTC: {epubPath} -> {outputPath}");
            Console.WriteLine($"Device: {options.Device.GetDisplayName()}  ({width}x{height})");
            Console.WriteLine($"Vertical Writing: {options.EnableVerticalWriting}");
            Console.WriteLine($"Font: {(string.IsNullOrWhiteSpace(options.FontFile) ? "(auto)" : options.FontFile)}");

            var result = EpubToXtc.Convert(epubPath, outputPath, options.ToRenderOptions());
            Console.WriteLine($"XTC file created at: {result.OutputPath} ({result.PageCount} pages)");
        }

        /// <summary>EPUB を XTC へ変換する（非同期）。</summary>
        public override async Task ConvertAsync(string epubPath, string outputPath, XtcOptions? options = null)
        {
            options ??= _options;
            await EpubToXtc.ConvertAsync(epubPath, outputPath, options.ToRenderOptions())
                .ConfigureAwait(false);
        }

        /// <summary>EPUB を XTC へ変換する（進捗通知つき）。</summary>
        public Task<XtcResult> ConvertAsync(string epubPath, string outputPath, XtcOptions options,
            IProgress<XtcProgress>? progress, CancellationToken cancellationToken = default)
        {
            return EpubToXtc.ConvertAsync(epubPath, outputPath, options.ToRenderOptions(), progress, cancellationToken);
        }
    }
}
