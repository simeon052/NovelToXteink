using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using SixLabors.ImageSharp;

namespace NovelToEink.XtcConverter
{
    /// <summary>
    /// Library for converting EPUB to XTC format for X4 Pro devices
    /// </summary>
    public static class XtcConverterLibrary
    {
        /// <summary>
        /// Converts an EPUB file to XTC format
        /// </summary>
        /// <param name="epubPath">Path to the EPUB file</param>
        /// <param name="xtcPath">Path to the output XTC file</param>
        /// <param name="options">Conversion options</param>
        /// <returns>True if conversion was successful</returns>
        public static bool ConvertEpubToXtc(string epubPath, string xtcPath, XtcOptions? options)
        {
            try
            {
                if (!File.Exists(epubPath))
                {
                    Console.WriteLine($"EPUB file not found: {epubPath}");
                    return false;
                }

                options ??= new XtcOptions();

                // 進捗ログは X4ProXtcConverter 側で出す。
                new X4ProXtcConverter(options).ConvertEpubToXtc(epubPath, xtcPath, options);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during conversion: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Converts an EPUB file to XTC format with default options
        /// </summary>
        /// <param name="epubPath">Path to the EPUB file</param>
        /// <param name="xtcPath">Path to the output XTC file</param>
        /// <returns>True if conversion was successful</returns>
        public static bool ConvertEpubToXtc(string epubPath, string xtcPath)
        {
            // Default options for X4 Pro
            var options = new XtcOptions();
            
            return ConvertEpubToXtc(epubPath, xtcPath, options);
        }

        /// <summary>
        /// Asynchronously converts an EPUB file to XTC format with default options
        /// </summary>
        /// <param name="epubPath">Path to the EPUB file</param>
        /// <param name="xtcPath">Path to the output XTC file</param>
        /// <returns>True if conversion was successful</returns>
        public static async Task<bool> ConvertEpubToXtcAsync(string epubPath, string xtcPath)
        {
            var converter = new XtcConverter();
            try
            {
                await converter.ConvertAsync(epubPath, xtcPath);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during conversion: {ex.Message}");
                return false;
            }
        }
    }
}
