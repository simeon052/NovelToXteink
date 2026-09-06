using System;
using System.IO;
using System.Threading.Tasks;
using NovelToEink.XtcConverter;

namespace NovelToEink.Core
{
    /// <summary>
    /// XTC変換ユーティリティ
    /// </summary>
    public static class XtcConversionUtility
    {
        /// <summary>
        /// EPUBファイルをXTCファイルに変換する
        /// </summary>
        /// <param name="epubPath">EPUBファイルパス</param>
        /// <param name="xtcPath">出力XTCファイルパス</param>
        /// <param name="options">変換オプション</param>
        public static void ConvertEpubToXtc(string epubPath, string xtcPath, XtcOptions options)
        {
            try
            {
                // Ensure the XTC directory exists
                var xtcDir = Path.GetDirectoryName(xtcPath);
                if (!string.IsNullOrEmpty(xtcDir) && !Directory.Exists(xtcDir))
                {
                    Directory.CreateDirectory(xtcDir);
                }

                var converter = new X4ProXtcConverter(options);
                converter.ConvertEpubToXtc(epubPath, xtcPath, options);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"XTC変換中にエラーが発生しました: {ex.Message}", ex);
            }
        }

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
                // Generate XTC file path
                var xtcPath = Path.ChangeExtension(epubPath, ".xtc");

                // Create XTC options
                var xtcOptions = new XtcOptions
                {
                    Resolution = (480, 800),
                    EnableVerticalWriting = epubOptions.WritingMode == WritingMode.Vertical,
                    FontFile = string.IsNullOrEmpty(epubOptions.XtcFontFile) ? null : epubOptions.XtcFontFile,
                    FontSize = 36,
                    RubyFontSize = 14,
                    LineSpacing = 56,
                    MarginTop = 12,
                    MarginBottom = 12,
                    MarginRight = 12
                };

                // Convert EPUB to XTC using the library
                ConvertEpubToXtc(epubPath, xtcPath, xtcOptions);
                
                Console.WriteLine($"XTCファイル生成完了: {xtcPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"XTC生成に失敗しました: {ex.Message}");
            }
        }
    }
}
