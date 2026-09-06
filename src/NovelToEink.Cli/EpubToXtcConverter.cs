using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NovelToEink.XtcConverter;

namespace NovelToEink.Cli
{
    /// <summary>
    /// CLI utility for batch converting EPUB files to XTC format
    /// </summary>
    public class EpubToXtcConverter
    {
        /// <summary>
        /// Converts all EPUB files in a directory to XTC format
        /// </summary>
        /// <param name="inputDirectory">Directory containing EPUB files</param>
        /// <param name="outputDirectory">Directory to save XTC files</param>
        /// <param name="options">Conversion options (device, font, layout)</param>
        /// <returns>Conversion statistics</returns>
        public static (int success, int failed) ConvertDirectory(string inputDirectory, string outputDirectory, XtcOptions? options = null)
        {
            if (!Directory.Exists(inputDirectory))
            {
                Console.WriteLine($"Error: Input directory does not exist: {inputDirectory}");
                return (0, 0);
            }

            // Ensure output directory exists
            Directory.CreateDirectory(outputDirectory);

            var epubFiles = Directory.GetFiles(inputDirectory, "*.epub", SearchOption.TopDirectoryOnly);
            int successCount = 0;
            int failedCount = 0;

            Console.WriteLine($"Found {epubFiles.Length} EPUB files to convert");

            foreach (var epubFile in epubFiles)
            {
                try
                {
                    var fileName = Path.GetFileNameWithoutExtension(epubFile);
                    var xtcFile = Path.Combine(outputDirectory, $"{fileName}.xtc");
                    
                    Console.WriteLine($"Converting: {fileName}");
                    
                    if (XtcConverterLibrary.ConvertEpubToXtc(epubFile, xtcFile, options ?? new XtcOptions()))
                    {
                        Console.WriteLine($"  ✓ Success: {xtcFile}");
                        successCount++;
                    }
                    else
                    {
                        Console.WriteLine($"  ✗ Failed: {epubFile}");
                        failedCount++;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ✗ Error processing {epubFile}: {ex.Message}");
                    failedCount++;
                }
            }

            Console.WriteLine($"\nConversion complete:");
            Console.WriteLine($"  Success: {successCount}");
            Console.WriteLine($"  Failed: {failedCount}");
            Console.WriteLine($"  Total: {epubFiles.Length}");

            return (successCount, failedCount);
        }
    }
}